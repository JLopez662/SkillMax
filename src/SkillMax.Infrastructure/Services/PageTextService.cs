using System.Text;
using System.Text.RegularExpressions;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Infrastructure.Persistence;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace SkillMax.Infrastructure.Services;

/// <summary>
/// Recovers a page's text as ordered blocks using PdfPig, and caches the result on disk so a page is
/// only parsed once. Pure managed code (no native deps), DB-free and thread-safe → registered as a
/// singleton alongside <see cref="PageImageService"/>.
///
/// Blocks come from Docstrum segmentation on extracted words, which is what makes paragraph-level
/// splitting possible; heading and list detection then run off font size and line shape. Anything we
/// can't read as text (scans, full-page plates) is reported as <see cref="PageContent.RequiresRender"/>
/// so the feed falls back to the rendered page image.
/// </summary>
public sealed class PageTextService : IPageTextService
{
    /// <summary>Below this many characters a page isn't worth setting as text.</summary>
    private const int MinUsableChars = 120;

    /// <summary>Fraction of page height at top and bottom treated as running head/foot.</summary>
    private const double MarginBand = 0.07;

    // Folios: "87", "xiv", "— 12 —". The roman half is spelled out strictly on purpose — a loose
    // [ivxlc]+ also matches real words like "civil" and would delete them from the page.
    private const string RomanNumeral = @"m{0,4}(?:cm|cd|d?c{0,3})(?:xc|xl|l?x{0,3})(?:ix|iv|v?i{0,3})";
    private static readonly Regex FolioOnly = new(
        $@"^[\s\.\-–—]*(?:\d{{1,4}}|{RomanNumeral})[\s\.\-–—]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ListItemStart = new(@"^(\d{1,3}[\.\)]|[a-zA-Z][\.\)]|[•▪◦·\-–—\*])\s+", RegexOptions.Compiled);
    private static readonly Regex FigureCaption = new(@"^(Figure|Fig\.?|Exhibit|Table|Chart)\s*\d", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SoftHyphenJoin = new(@"(\p{Ll})-\s(\p{Ll})", RegexOptions.Compiled);

    private readonly SkillMaxStorage _storage;
    private readonly SemaphoreSlim _parseGate = new(1, 1);

    public PageTextService(SkillMaxStorage storage) => _storage = storage;

    public async Task<PageContent> GetPageContentAsync(
        int bookId, string pdfPath, int pageIndex, CancellationToken ct = default)
    {
        // The suffix is a cache version: v1 discarded the text of thin pages, so those entries would
        // keep serving a page render to the reader. Bumping it re-extracts instead of going stale.
        var cachePath = Path.Combine(_storage.TextCacheDir, $"{bookId}_{pageIndex}.v2.txt");

        if (File.Exists(cachePath))
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(cachePath, ct).ConfigureAwait(false);
                return Decode(pageIndex, lines);
            }
            catch { /* unreadable cache entry: fall through and re-extract */ }
        }

        PageContent content;
        try
        {
            content = await ExtractAsync(pdfPath, pageIndex, ct).ConfigureAwait(false);
        }
        catch
        {
            content = PageContent.ImageOnly(pageIndex);
        }

        try { await File.WriteAllLinesAsync(cachePath, Encode(content), ct).ConfigureAwait(false); }
        catch { /* cache is best-effort */ }

        return content;
    }

    private async Task<PageContent> ExtractAsync(string pdfPath, int pageIndex, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(pdfPath, ct).ConfigureAwait(false);

        await _parseGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var doc = PdfDocument.Open(bytes);
                if (pageIndex < 0 || pageIndex >= doc.NumberOfPages) return PageContent.ImageOnly(pageIndex);

                var page = doc.GetPage(pageIndex + 1);
                var words = page.GetWords(new NearestNeighbourWordExtractor()).ToList();
                if (words.Count == 0) return PageContent.ImageOnly(pageIndex);

                var blocks = BuildBlocks(page, words);
                var chars = blocks.Sum(b => b.Text.Length);
                var hasFigure = blocks.Any(b => b.Kind == PageBlockKind.Figure);

                // Too little text to carry a card on its own — but the words are still kept. A card
                // shows the page render; the reader, which has no box to fill, flows what's there.
                var thin = chars < MinUsableChars && !hasFigure;

                return new PageContent
                {
                    PageIndex = pageIndex,
                    Blocks = blocks,
                    RequiresRender = thin,
                };
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _parseGate.Release();
        }
    }

    private static List<PageBlock> BuildBlocks(Page page, List<Word> words)
    {
        // Single-column reading order: top down, then left to right. PdfPig's Y axis grows upward.
        var segmented = new DocstrumBoundingBoxes()
            .GetBlocks(words)
            .OrderByDescending(b => b.BoundingBox.Top)
            .ThenBy(b => b.BoundingBox.Left)
            .ToList();

        var bodySize = MedianPointSize(words);
        var pageTop = page.Height * (1 - MarginBand);
        var pageBottom = page.Height * MarginBand;

        var result = new List<PageBlock>(segmented.Count);
        var figureTaken = false;

        foreach (var block in segmented)
        {
            var text = Normalize(JoinLines(block));
            if (text.Length == 0) continue;

            // Running heads, folios and stray page numbers.
            var inMargin = block.BoundingBox.Bottom > pageTop || block.BoundingBox.Top < pageBottom;
            if (FolioOnly.IsMatch(text)) continue;
            if (inMargin && text.Length <= 60) continue;

            // Worksheet rules and dot leaders: mostly punctuation, nothing to read.
            if (!MostlyLetters(text)) continue;

            // A caption is the figure: keep the words, and let the slide carry the page render.
            // Captions run to two sentences in this kind of book ("Figure 13.1 The execution journey
            // is first a thinking journey. If you think that…"), and a vector diagram leaves no
            // embedded image for the fallback below to find — so the caption is the only signal that
            // there's something to show.
            if (!figureTaken && FigureCaption.IsMatch(text) && text.Length <= 240)
            {
                result.Add(new PageBlock(PageBlockKind.Figure, text));
                figureTaken = true;
                continue;
            }

            var kind = Classify(block, text, bodySize);

            // Segmentation clumps a short list into one box ("1. Vision 2. Planning 3. …").
            // Split it back per line so each item keeps its own line — and its own height cost.
            if (kind == PageBlockKind.ListItem && block.TextLines.Count > 1)
            {
                var items = SplitListLines(block);
                if (items.Count > 1)
                {
                    result.AddRange(items.Select(t => new PageBlock(PageBlockKind.ListItem, t)));
                    continue;
                }
            }

            // A title broken across lines arrives as two boxes ("Long-Term Capacity versus Short-"
            // / "Term Results"); stitch it back, healing the hyphen.
            if (kind == PageBlockKind.Heading && result.Count > 0 &&
                result[^1].Kind == PageBlockKind.Heading &&
                result[^1].Text.Length + text.Length <= 90)
            {
                result[^1] = new PageBlock(PageBlockKind.Heading, JoinHeading(result[^1].Text, text));
                continue;
            }

            result.Add(new PageBlock(kind, text));
        }

        // An uncaptioned plate on an otherwise textual page still needs showing.
        if (!figureTaken && page.NumberOfImages > 0 && LargestImageShare(page) > 0.2)
            result.Add(new PageBlock(PageBlockKind.Figure, ""));

        return result;
    }

    private static PageBlockKind Classify(TextBlock block, string text, double bodySize)
    {
        if (ListItemStart.IsMatch(text) && text.Length <= 240) return PageBlockKind.ListItem;

        var lines = block.TextLines.Count;
        var size = MedianPointSize(block.TextLines.SelectMany(l => l.Words));
        var endsMidSentence = text.EndsWith('.') || text.EndsWith(',') || text.EndsWith(';');

        // Bigger type than the body, short, and not a sentence: a heading.
        if (bodySize > 0 && size > bodySize * 1.12 && lines <= 3 && text.Length <= 90 && !endsMidSentence)
            return PageBlockKind.Heading;

        // Small-caps / all-caps section labels set at body size.
        if (lines <= 2 && text.Length <= 60 && text == text.ToUpperInvariant() && text.Any(char.IsLetter))
            return PageBlockKind.Heading;

        // Bold run-in headings ("Criteria 1: Make them specific.").
        var bold = block.TextLines.SelectMany(l => l.Words)
            .Any(w => w.FontName?.Contains("bold", StringComparison.OrdinalIgnoreCase) == true);
        if (bold && lines == 1 && text.Length <= 80 && !endsMidSentence) return PageBlockKind.Heading;

        return PageBlockKind.Paragraph;
    }

    /// <summary>One list item per typeset line, when the lines really are separate items.</summary>
    private static List<string> SplitListLines(TextBlock block)
    {
        var lines = block.TextLines
            .Select(l => Normalize(l.Text))
            .Where(t => t.Length > 0)
            .ToList();

        // Only split when most lines open with their own marker; otherwise it's one wrapped item.
        var marked = lines.Count(t => ListItemStart.IsMatch(t));
        return marked >= Math.Max(2, lines.Count - 1) ? lines : new List<string>();
    }

    /// <summary>
    /// Rejoins a title split across boxes. A trailing hyphen is only a line-break artefact when the
    /// next fragment continues in lower case — "Short-" + "Term Results" is a real hyphenated word
    /// and has to keep it.
    /// </summary>
    private static string JoinHeading(string first, string second)
    {
        if (second.Length == 0) return first;
        if (!first.EndsWith('-')) return first + " " + second;
        return char.IsLower(second[0]) ? first[..^1] + second : first + second;
    }

    /// <summary>Guards against form rules, dot leaders and other typographic furniture.</summary>
    private static bool MostlyLetters(string text)
    {
        var letters = text.Count(char.IsLetter);
        return letters >= 3 && letters >= text.Length * 0.4;
    }

    /// <summary>
    /// Joins a block's lines back into flowing text, healing words the typesetter broke across
    /// lines — "capa-\ncity" has to come back as "capacity", not "capa- city".
    /// </summary>
    private static string JoinLines(TextBlock block)
    {
        var sb = new StringBuilder();
        foreach (var line in block.TextLines)
        {
            var t = line.Text.Trim();
            if (t.Length == 0) continue;

            if (sb.Length == 0) { sb.Append(t); continue; }

            if (sb[^1] == '-' && t.Length > 0 && char.IsLower(t[0]))
                sb.Length -= 1;              // hyphenated break: drop the hyphen, no space
            else
                sb.Append(' ');

            sb.Append(t);
        }
        return sb.ToString();
    }

    private static string Normalize(string text)
    {
        text = Whitespace.Replace(text, " ").Trim();
        text = SoftHyphenJoin.Replace(text, "$1$2");
        text = text.Replace("--", "—");
        return text;
    }

    private static double MedianPointSize(IEnumerable<Word> words)
    {
        var sizes = words
            .SelectMany(w => w.Letters)
            .Select(l => l.PointSize)
            .Where(s => s > 0)
            .OrderBy(s => s)
            .ToList();

        return sizes.Count == 0 ? 0 : sizes[sizes.Count / 2];
    }

    /// <summary>Area of the biggest embedded image as a share of the page, for figure detection.</summary>
    private static double LargestImageShare(Page page)
    {
        var pageArea = page.Width * page.Height;
        if (pageArea <= 0) return 0;

        try
        {
            var biggest = page.GetImages().Select(i => i.BoundingBox.Area).DefaultIfEmpty(0).Max();
            return biggest / pageArea;
        }
        catch
        {
            return 0;   // malformed image dictionaries shouldn't cost us the page's text
        }
    }

    // ---- disk cache: one block per line, "<kind>\t<text>". Trim-safe, no serializer. ----

    private const string RenderMarker = "!render";

    private static IEnumerable<string> Encode(PageContent content)
    {
        // The marker is a header, not a substitute for the body: a thin page still keeps its words
        // so the reader can flow them even though a card would show the render.
        if (content.RequiresRender) yield return RenderMarker;

        foreach (var b in content.Blocks)
            yield return $"{KindCode(b.Kind)}\t{b.Text}";
    }

    private static PageContent Decode(int pageIndex, string[] lines)
    {
        if (lines.Length == 0) return PageContent.ImageOnly(pageIndex);

        var requiresRender = lines[0] == RenderMarker;

        var blocks = new List<PageBlock>(lines.Length);
        foreach (var line in lines)
        {
            var tab = line.IndexOf('\t');
            if (tab < 1) continue;
            blocks.Add(new PageBlock(KindFrom(line[0]), line[(tab + 1)..]));
        }

        if (blocks.Count == 0) return PageContent.ImageOnly(pageIndex);

        return new PageContent
        {
            PageIndex = pageIndex,
            Blocks = blocks,
            RequiresRender = requiresRender,
        };
    }

    private static char KindCode(PageBlockKind kind) => kind switch
    {
        PageBlockKind.Heading => 'H',
        PageBlockKind.ListItem => 'L',
        PageBlockKind.Figure => 'F',
        _ => 'P',
    };

    private static PageBlockKind KindFrom(char code) => code switch
    {
        'H' => PageBlockKind.Heading,
        'L' => PageBlockKind.ListItem,
        'F' => PageBlockKind.Figure,
        _ => PageBlockKind.Paragraph,
    };
}
