using SkillMax.Application.Models;

namespace SkillMax.Application.Abstractions;

public interface IChapterService
{
    /// <summary>
    /// Which chapter a page belongs to, from the PDF's own outline where it has one. Returns null when
    /// the book's structure can't be established — callers hide the entry point rather than guess a
    /// range. Cached per book.
    /// </summary>
    Task<ChapterRef?> GetForPageAsync(int bookId, string pdfPath, int pageIndex, CancellationToken ct = default);

    /// <summary>The chapter following <paramref name="current"/>, for reading straight on.</summary>
    Task<ChapterRef?> GetNextAsync(int bookId, string pdfPath, ChapterRef current, CancellationToken ct = default);

    /// <summary>
    /// The book's whole contents list, from the PDF's own outline. Empty when the book hasn't got
    /// one: the walk that answers <see cref="GetForPageAsync"/> finds a chapter around a page you're
    /// already on, which is no basis for enumerating a book, so the contents screen says so rather
    /// than showing a list it made up.
    /// </summary>
    Task<IReadOnlyList<BookChapter>> GetChaptersAsync(int bookId, string pdfPath, CancellationToken ct = default);
}
