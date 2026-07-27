using System.Text.RegularExpressions;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Infrastructure.Persistence;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace SkillMax.Infrastructure.Services;

/// <summary>
/// Works out a book's chapter structure, preferring the PDF's own outline — which gives exact titles
/// and page numbers with no guessing, once <see cref="ChapterLevel"/> has worked out which depth of
/// it holds the chapters. Books published without an outline fall back to walking outward from the
/// page for a "Chapter N" heading in the text layer; that walk is bounded, and whatever it finds is
/// remembered so the next lookup nearby is free.
///
/// Cached as one file per book alongside the page-text cache, so there's no schema change and
/// <c>BookService</c>'s existing cleanup already removes it with the book.
/// </summary>
public sealed class ChapterService : IChapterService
{
    /// <summary>How far the fallback walk looks for a chapter heading before giving up.</summary>
    private const int WalkLimit = 45;

    /// <summary>Fewer entries than this and a level is front matter or a wrapper, not a chapter list.</summary>
    private const int MinChapters = 3;

    /// <summary>An entry spanning more than this share of the book wraps it rather than divides it.</summary>
    private const double WrapperSpan = 0.9;

    /// <summary>Bumped when the cached layout or the rule that produced it changes, to force a rebuild.</summary>
    private const string CacheVersion = "v2";

    private static readonly Regex ChapterHeading = new(
        @"^(chapter|part|law)\s+([0-9]{1,3}|[ivxlcdm]{1,7})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly SkillMaxStorage _storage;
    private readonly IPageTextService _text;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ChapterService(SkillMaxStorage storage, IPageTextService text)
    {
        _storage = storage;
        _text = text;
    }

    public async Task<ChapterRef?> GetForPageAsync(
        int bookId, string pdfPath, int pageIndex, CancellationToken ct = default)
    {
        var page = pageIndex + 1;
        var outline = await LoadAsync(bookId, pdfPath, ct).ConfigureAwait(false);

        if (outline.Complete) return FromOutline(outline, page);

        // Partial (scanned) structure: answer from what we know, else go and find it.
        var known = FromScanned(outline, page);
        if (known is not null) return known;

        var discovered = await ScanAroundAsync(bookId, pdfPath, page, outline, ct).ConfigureAwait(false);
        return discovered;
    }

    public async Task<ChapterRef?> GetNextAsync(
        int bookId, string pdfPath, ChapterRef current, CancellationToken ct = default)
    {
        if (current.EndPage >= await PageCountAsync(bookId, pdfPath, ct).ConfigureAwait(false)) return null;
        return await GetForPageAsync(bookId, pdfPath, current.EndPage, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BookChapter>> GetChaptersAsync(
        int bookId, string pdfPath, CancellationToken ct = default)
    {
        var outline = await LoadAsync(bookId, pdfPath, ct).ConfigureAwait(false);
        if (!outline.Complete) return Array.Empty<BookChapter>();

        // One entry per starting page: a producer that emits the same page twice would otherwise
        // yield a chapter ending before it begins.
        var starts = outline.Entries
            .Where(e => e.Level == 0)
            .GroupBy(e => e.Page)
            .Select(g => g.First())
            .OrderBy(e => e.Page)
            .ToList();

        var chapters = new List<BookChapter>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i].Page;
            var end = i + 1 < starts.Count ? starts[i + 1].Page - 1 : outline.PageCount;

            var sections = outline.Entries
                .Where(e => e.Level == 1 && e.Page >= start && e.Page <= end)
                .OrderBy(e => e.Page)
                .Select(e => new ChapterSection(e.Title, e.Page))
                .ToList();

            chapters.Add(new BookChapter(
                new ChapterRef { Title = starts[i].Title, StartPage = start, EndPage = end },
                sections));
        }

        return chapters;
    }

    // ---- outline lookup ----

    private static ChapterRef? FromOutline(Outline outline, int page)
    {
        var chapter = outline.Entries
            .Where(e => e.Level == 0 && e.Page <= page)
            .OrderBy(e => e.Page)
            .LastOrDefault();
        if (chapter is null) return null;

        var next = outline.Entries
            .Where(e => e.Level == 0 && e.Page > page)
            .OrderBy(e => e.Page)
            .FirstOrDefault();

        var section = outline.Entries
            .Where(e => e.Level == 1 && e.Page <= page && e.Page >= chapter.Page)
            .OrderBy(e => e.Page)
            .LastOrDefault();

        return new ChapterRef
        {
            Title = chapter.Title,
            Section = section?.Title,
            StartPage = chapter.Page,
            EndPage = (next?.Page - 1) ?? outline.PageCount,
        };
    }

    /// <summary>A scanned structure can only answer when we've found the starts on both sides.</summary>
    private static ChapterRef? FromScanned(Outline outline, int page)
    {
        var starts = outline.Entries.Select(e => e.Page).OrderBy(p => p).ToList();
        if (starts.Count == 0) return null;

        var start = starts.LastOrDefault(p => p <= page);
        if (start == 0) return null;

        var next = starts.FirstOrDefault(p => p > page);
        if (next == 0 && outline.ScannedTo < outline.PageCount) return null;   // right edge unknown

        var entry = outline.Entries.First(e => e.Page == start);
        return new ChapterRef
        {
            Title = entry.Title,
            StartPage = start,
            EndPage = next == 0 ? outline.PageCount : next - 1,
        };
    }

    /// <summary>
    /// Walks back from the page for a chapter heading, then forward for the following one, and folds
    /// both into the cache. Bounded by <see cref="WalkLimit"/> in each direction.
    /// </summary>
    private async Task<ChapterRef?> ScanAroundAsync(
        int bookId, string pdfPath, int page, Outline outline, CancellationToken ct)
    {
        var startEntry = await WalkAsync(bookId, pdfPath, page, back: true, outline.PageCount, ct)
            .ConfigureAwait(false);
        if (startEntry is null) return null;

        var nextEntry = await WalkAsync(bookId, pdfPath, startEntry.Page + 1, back: false, outline.PageCount, ct)
            .ConfigureAwait(false);

        var entries = outline.Entries.ToList();
        Remember(entries, startEntry);
        if (nextEntry is not null) Remember(entries, nextEntry);

        var scannedTo = Math.Max(outline.ScannedTo, nextEntry?.Page ?? Math.Min(outline.PageCount, page + WalkLimit));
        var updated = new Outline(false, outline.PageCount, entries, scannedTo);
        await SaveAsync(bookId, updated, ct).ConfigureAwait(false);

        return new ChapterRef
        {
            Title = startEntry.Title,
            StartPage = startEntry.Page,
            EndPage = (nextEntry?.Page - 1) ?? outline.PageCount,
        };
    }

    private static void Remember(List<Entry> entries, Entry e)
    {
        if (entries.All(x => x.Page != e.Page)) entries.Add(e);
    }

    private async Task<Entry?> WalkAsync(
        int bookId, string pdfPath, int from, bool back, int pageCount, CancellationToken ct)
    {
        var step = back ? -1 : 1;
        for (var i = 0; i < WalkLimit; i++)
        {
            var page = from + i * step;
            if (page < 1 || page > pageCount) return null;

            var content = await _text.GetPageContentAsync(bookId, pdfPath, page - 1, ct).ConfigureAwait(false);
            var heading = content.Blocks
                .FirstOrDefault(b => b.Kind == PageBlockKind.Heading && ChapterHeading.IsMatch(b.Text));

            if (heading is not null) return new Entry(0, page, heading.Text);
        }
        return null;
    }

    // ---- cache ----

    private async Task<Outline> LoadAsync(int bookId, string pdfPath, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = CachePath(bookId);
            if (File.Exists(path))
            {
                try
                {
                    var cached = Decode(await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false));
                    if (cached is not null) return cached;
                }
                catch { /* unreadable: rebuild below */ }
            }

            var built = await Task.Run(() => ReadOutline(pdfPath), ct).ConfigureAwait(false);
            try { await File.WriteAllLinesAsync(path, Encode(built), ct).ConfigureAwait(false); }
            catch { /* cache is best-effort */ }
            return built;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(int bookId, Outline outline, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { await File.WriteAllLinesAsync(CachePath(bookId), Encode(outline), ct).ConfigureAwait(false); }
        catch { /* best-effort */ }
        finally { _gate.Release(); }
    }

    private string CachePath(int bookId) => Path.Combine(_storage.TextCacheDir, $"{bookId}_outline.txt");

    private static Outline ReadOutline(string pdfPath)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            var pageCount = doc.NumberOfPages;

            if (!doc.TryGetBookmarks(out var bookmarks))
                return new Outline(false, pageCount, new List<Entry>(), 0);

            var all = bookmarks.GetNodes()
                .OfType<DocumentBookmarkNode>()
                .Where(n => n.PageNumber >= 1 && !string.IsNullOrWhiteSpace(n.Title))
                .Select(n => new Entry(n.Level, n.PageNumber, n.Title.Trim()))
                .OrderBy(e => e.Page)
                .ToList();

            // An outline whose chapters we can't locate tells us nothing.
            var chapterLevel = ChapterLevel(all, pageCount);
            if (chapterLevel < 0) return new Outline(false, pageCount, new List<Entry>(), 0);

            // Re-base onto the levels the rest of this class expects: chapters at 0, their
            // subsections at 1, whatever depth they were nested at in the file.
            var entries = all
                .Where(e => e.Level == chapterLevel || e.Level == chapterLevel + 1)
                .Select(e => e with { Level = e.Level - chapterLevel })
                .ToList();

            return new Outline(true, pageCount, entries, pageCount);
        }
        catch
        {
            return new Outline(false, 0, new List<Entry>(), 0);
        }
    }

    /// <summary>
    /// Finds the depth the chapters actually sit at. Nearly always 0, but some producers — Internet
    /// Archive's converter among them — nest the whole book under a single title node, leaving the
    /// chapters a level down; taking level 0 there yields one "chapter" spanning the entire book.
    /// Descends past any level too sparse to be a chapter list, or whose entries don't divide the
    /// book. Returns -1 when no level does.
    /// </summary>
    private static int ChapterLevel(List<Entry> entries, int pageCount)
    {
        if (entries.Count == 0 || pageCount <= 0) return -1;

        var deepest = entries.Max(e => e.Level);
        for (var level = 0; level <= deepest; level++)
        {
            var starts = entries.Where(e => e.Level == level).Select(e => e.Page).OrderBy(p => p).ToList();
            if (starts.Count < MinChapters) continue;

            // Spans run start-to-next-start, the last one to the end of the book — which is what
            // exposes a trailing wrapper, whose one entry swallows everything after it.
            var widest = starts.Zip(starts.Skip(1).Append(pageCount + 1), (a, b) => b - a).Max();
            if (widest > pageCount * WrapperSpan) continue;

            return level;
        }

        // Nothing qualified — a short outline is still better structure than none.
        return entries.Any(e => e.Level == 0) ? 0 : -1;
    }

    private async Task<int> PageCountAsync(int bookId, string pdfPath, CancellationToken ct) =>
        (await LoadAsync(bookId, pdfPath, ct).ConfigureAwait(false)).PageCount;

    // One header line, then "<level>\t<page>\t<title>" per entry.
    private static IEnumerable<string> Encode(Outline o)
    {
        yield return $"{CacheVersion}\t{(o.Complete ? 1 : 0)}\t{o.PageCount}\t{o.ScannedTo}";
        foreach (var e in o.Entries) yield return $"{e.Level}\t{e.Page}\t{e.Title}";
    }

    /// <summary>Null when the file was written by an older version, whose levels can't be trusted.</summary>
    private static Outline? Decode(string[] lines)
    {
        if (lines.Length == 0) return null;

        var head = lines[0].Split('\t');
        if (head.Length < 4 || head[0] != CacheVersion) return null;

        var complete = head[1] == "1";
        var pageCount = int.TryParse(head[2], out var pc) ? pc : 0;
        var scannedTo = int.TryParse(head[3], out var st) ? st : 0;

        var entries = new List<Entry>(lines.Length);
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split('\t', 3);
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0], out var level) || !int.TryParse(parts[1], out var page)) continue;
            entries.Add(new Entry(level, page, parts[2]));
        }

        return new Outline(complete, pageCount, entries, scannedTo);
    }

    private sealed record Entry(int Level, int Page, string Title);

    /// <param name="Complete">True when it came from a real outline, so absence of an entry is meaningful.</param>
    /// <param name="ScannedTo">How far the fallback walk has reached, for partial structures.</param>
    private sealed record Outline(bool Complete, int PageCount, List<Entry> Entries, int ScannedTo);
}
