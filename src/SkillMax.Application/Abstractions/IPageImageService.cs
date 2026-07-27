namespace SkillMax.Application.Abstractions;

public interface IPageImageService
{
    /// <summary>
    /// Render (and cache) one PDF page as a PNG data URI ready for an <c>&lt;img src&gt;</c>.
    /// Falls back to a placeholder graphic if the page can't be rendered.
    /// </summary>
    Task<string> GetPageDataUriAsync(int bookId, string pdfPath, int pageIndex, CancellationToken ct = default);

    /// <summary>
    /// The book's cover — page 1, rendered small and cached separately from the reader's
    /// full-size pages, because a shelf shows many at once. Returns null rather than a
    /// placeholder when it can't be rendered, so the caller keeps its own fallback.
    /// </summary>
    Task<string?> GetCoverDataUriAsync(int bookId, string pdfPath, CancellationToken ct = default);
}
