namespace SkillMax.Application.Models;

/// <summary>
/// One item in the feed: a page from a book plus the metadata the card chrome needs.
/// The page's content is fetched lazily from <see cref="PdfPath"/> + <see cref="PageIndex"/> —
/// its text via <c>IPageTextService</c> (split into slides by <c>PageSplitter</c>), and its
/// rendered image via <c>IPageImageService</c> for figures and pages with no text layer.
/// </summary>
public sealed record FeedCard
{
    public int BookId { get; init; }
    public required string BookTitle { get; init; }
    public string? Author { get; init; }

    /// <summary>Absolute path to the source PDF (used to render the page image).</summary>
    public required string PdfPath { get; init; }

    /// <summary>0-based page index.</summary>
    public int PageIndex { get; init; }
    public int PageCount { get; init; }

    public string ColorHex { get; init; } = "#4f5bd5";
    public int ProgressPct { get; init; }
    public bool Saved { get; init; }

    /// <summary>The reader has marked this page understood, so it's drawn less often.</summary>
    public bool Understood { get; init; }

    /// <summary>
    /// Which background this page's slides are set on. Assigned when the feed is built so the choice
    /// can take the card above into account; see <see cref="CardBackgrounds.For"/>.
    /// </summary>
    public CardBackground Background { get; init; } = CardBackgrounds.Pool[0];

    public int PageNumber => PageIndex + 1;

    /// <summary>Instagram-style handle derived from the title, e.g. "the_pragmatic_programmer".</summary>
    public string Handle => HandleFor(BookTitle);

    public static string HandleFor(string title)
    {
        var chars = title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_');
        var slug = new string(chars.ToArray());
        while (slug.Contains("__")) slug = slug.Replace("__", "_");
        return slug.Trim('_');
    }
}
