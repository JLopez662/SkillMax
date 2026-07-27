using SkillMax.Domain.Entities;

namespace SkillMax.Application.Abstractions;

public interface IBookService
{
    /// <summary>
    /// Every book on the shelf, paused ones included — the Library shows them greyed rather than
    /// hiding them, so a book you turned off is still somewhere you can turn back on. Callers that
    /// want only what feeds the app should filter on <see cref="Book.IsArchived"/>.
    /// </summary>
    Task<IReadOnlyList<Book>> GetBooksAsync(CancellationToken ct = default);

    Task<Book?> GetBookAsync(int id, CancellationToken ct = default);

    /// <summary>Counts only books that feed the app, for the profile.</summary>
    Task<int> GetBookCountAsync(CancellationToken ct = default);

    Task<int> GetPagesSeenTotalAsync(CancellationToken ct = default);

    /// <summary>
    /// Pause or resume a book. A paused book keeps its pages, marks and progress; it just stops
    /// being drawn into the feed — <c>FeedService</c> already skips it.
    /// </summary>
    Task SetEnabledAsync(int id, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Retitle a book. Import can only name a book after its file, which is rarely what it's
    /// called. An empty title is refused rather than stored — a book has to be called something.
    /// </summary>
    Task RenameAsync(int id, string title, string? author, CancellationToken ct = default);

    Task DeleteBookAsync(int id, CancellationToken ct = default);
}
