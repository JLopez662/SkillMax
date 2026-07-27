using System.Text.RegularExpressions;
using SkillMax.Application.Models;

namespace SkillMax.Application;

/// <summary>
/// Splits one page's text into the slides of a single post. Pure and deterministic, so a page always
/// breaks the same way.
///
/// The rules, in order of precedence:
///   1. Paragraphs are the seam. Slides fill greedily with whole blocks.
///   2. A paragraph bigger than a slide splits at sentence boundaries into near-equal parts; the
///      continuing part is flagged so the UI can label it.
///   3. A heading never ends a slide — the next block comes with it, even past the target.
///   4. A trailing slide under a third of the target merges back into the one before it.
///
/// The target is a <em>height</em> budget expressed in characters: every block also pays
/// <see cref="BlockCost"/> for the line and gap it occupies, because five one-line list items eat a
/// third of a card while contributing sixty characters. Packing on raw length overfills exactly
/// those pages. Final type size is not decided here — the WebView measures each slide and takes the
/// largest tier that fits.
/// </summary>
public static class PageSplitter
{
    /// <summary>
    /// Chosen by measuring against a 4:5 card <see cref="ReferenceWidth"/> wide: most slides land at
    /// 19–21px type with roughly 68px to spare. Raising it trades swipes for smaller text.
    /// </summary>
    public const int DefaultTarget = 520;

    /// <summary>The card width the default target was calibrated against, in CSS pixels.</summary>
    public const double ReferenceWidth = 393;

    /// <summary>
    /// The target scaled to the card the text actually has to fit. A 4:5 card's area grows with the
    /// square of its width, so the amount of text it holds does too — on a 310px-wide card the 520
    /// budget overfills badly enough that even the smallest type tier clips, which is a defect the
    /// reader can see. Clamped so an extreme viewport can't produce one-line or unreadable slides.
    /// </summary>
    public static int TargetFor(double cardWidthCss)
    {
        if (cardWidthCss <= 0) return DefaultTarget;

        var scale = cardWidthCss / ReferenceWidth;
        var target = DefaultTarget * scale * scale;
        return (int)Math.Round(Math.Clamp(target, 240, 760));
    }

    /// <summary>What a block costs beyond its characters: one line plus the gap after it.</summary>
    private const int BlockCost = 52;

    // Trailing quote/bracket stays with its sentence, curly quotes included — otherwise a slide
    // can open with an orphaned ” from the sentence before it.
    private static readonly Regex Sentences = new(
        @"[^.!?]+(?:[.!?]+[""'”’)\]]*|$)\s*", RegexOptions.Compiled);

    public static IReadOnlyList<CardSlide> Split(
        IReadOnlyList<PageBlock> blocks, int target = DefaultTarget)
    {
        if (blocks.Count == 0) return Array.Empty<CardSlide>();

        var units = Atomize(blocks, target);
        var slides = Pack(units, target);
        MergeThinTail(slides, target);

        return slides.Select(ToSlide).ToList();
    }

    /// <summary>Rule 2: break paragraphs that can't fit a slide into sentence-sized units.</summary>
    private static List<SlideBlock> Atomize(IReadOnlyList<PageBlock> blocks, int target)
    {
        var units = new List<SlideBlock>(blocks.Count);

        foreach (var b in blocks)
        {
            if (b.Kind != PageBlockKind.Paragraph || b.Text.Length <= target * 1.35)
            {
                units.Add(new SlideBlock(b.Kind, b.Text));
                continue;
            }

            var parts = new List<string>();
            var buf = new System.Text.StringBuilder();
            foreach (Match m in Sentences.Matches(b.Text))
            {
                if (buf.Length > 0 && buf.Length + m.Length > target)
                {
                    parts.Add(buf.ToString().Trim());
                    buf.Clear();
                }
                buf.Append(m.Value);
            }
            if (buf.ToString().Trim().Length > 0) parts.Add(buf.ToString().Trim());

            for (var i = 0; i < parts.Count; i++)
                units.Add(new SlideBlock(PageBlockKind.Paragraph, parts[i], Continued: i > 0));
        }

        return units;
    }

    /// <summary>Rules 1 and 3: greedy fill on height cost, keeping headings with their text.</summary>
    private static List<List<SlideBlock>> Pack(List<SlideBlock> units, int target)
    {
        var slides = new List<List<SlideBlock>>();
        var cur = new List<SlideBlock>();
        var len = 0.0;

        void Flush()
        {
            if (cur.Count == 0) return;
            slides.Add(cur);
            cur = new List<SlideBlock>();
            len = 0;
        }

        foreach (var u in units)
        {
            // A figure is its own slide: it's a render, not text, so nothing shares the card.
            if (u.Kind == PageBlockKind.Figure)
            {
                Flush();
                slides.Add(new List<SlideBlock> { u });
                continue;
            }

            var w = Weight(u);
            if (cur.Count == 0)
            {
                cur.Add(u);
                len = w;
                continue;
            }

            var headingOnly = cur.All(x => x.Kind == PageBlockKind.Heading);
            var cap = target * (headingOnly ? 1.6 : 1.05);

            if (len + w <= cap)
            {
                cur.Add(u);
                len += w;
            }
            else
            {
                Flush();
                cur.Add(u);
                len = w;
            }
        }

        Flush();
        KeepHeadingsWithTheirText(slides);
        return slides;
    }

    /// <summary>
    /// Rule 3, enforced rather than merely encouraged: the cap above usually keeps a heading with
    /// the text under it, but a very long following paragraph can still push past it and leave the
    /// heading stranded. Pull text back up until the slide has something to say.
    /// </summary>
    private static void KeepHeadingsWithTheirText(List<List<SlideBlock>> slides)
    {
        for (var i = 0; i < slides.Count - 1; i++)
        {
            while (i < slides.Count - 1 && slides[i].All(u => u.Kind == PageBlockKind.Heading))
            {
                var next = slides[i + 1];

                // A figure is its own slide; a heading can't absorb one.
                if (next[0].Kind == PageBlockKind.Figure) break;

                slides[i].Add(next[0]);
                next.RemoveAt(0);
                if (next.Count == 0) slides.RemoveAt(i + 1);
            }
        }
    }

    /// <summary>Rule 4: a nearly-empty last slide is worse than a fuller second-to-last one.</summary>
    private static void MergeThinTail(List<List<SlideBlock>> slides, int target)
    {
        if (slides.Count < 2) return;

        var last = slides[^1];
        var prev = slides[^2];
        if (last[0].Kind == PageBlockKind.Figure || prev[0].Kind == PageBlockKind.Figure) return;

        if (Weigh(last) < target * 0.34 && Weigh(last) + Weigh(prev) <= target * 1.15)
        {
            prev.AddRange(last);
            slides.RemoveAt(slides.Count - 1);
        }
    }

    private static CardSlide ToSlide(List<SlideBlock> units) =>
        units[0].Kind == PageBlockKind.Figure
            ? new CardSlide { IsRender = true, Caption = units[0].Text }
            : new CardSlide { Blocks = units };

    private static double Weight(SlideBlock u)
    {
        var body = u.Kind == PageBlockKind.Heading
            ? Math.Max(u.Text.Length, 24) * 1.35
            : Math.Max(u.Text.Length, 18);
        return body + BlockCost;
    }

    private static double Weigh(IEnumerable<SlideBlock> units) => units.Sum(Weight);
}
