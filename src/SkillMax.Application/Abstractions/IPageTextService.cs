using SkillMax.Application.Models;

namespace SkillMax.Application.Abstractions;

public interface IPageTextService
{
    /// <summary>
    /// Recover one page's text as ordered blocks (headings, paragraphs, list items, figures), cached
    /// on disk after the first read. Returns <see cref="PageContent.RequiresRender"/> when the page
    /// has no usable text layer, so the caller falls back to <see cref="IPageImageService"/>.
    /// </summary>
    Task<PageContent> GetPageContentAsync(int bookId, string pdfPath, int pageIndex, CancellationToken ct = default);
}
