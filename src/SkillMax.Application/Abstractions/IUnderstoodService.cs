namespace SkillMax.Application.Abstractions;

/// <summary>
/// "I've seen this and I get it." Drives the heart on a post, and through it how often the feed
/// brings that post back — see <see cref="FeedWeighting"/>.
/// </summary>
public interface IUnderstoodService
{
    /// <summary>Mark or unmark a book page. Returns the new state.</summary>
    Task<bool> TogglePageAsync(int bookId, int pageIndex, CancellationToken ct = default);

    /// <summary>Mark or unmark a short. Returns the new state.</summary>
    Task<bool> ToggleShortAsync(string videoId, CancellationToken ct = default);

    /// <summary>How many posts have been marked, for the profile counter.</summary>
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>
    /// The 0-based page indexes marked in one book. The only per-page signal the app keeps, so it's
    /// what the contents screen counts per chapter.
    /// </summary>
    Task<IReadOnlyList<int>> GetPagesAsync(int bookId, CancellationToken ct = default);
}
