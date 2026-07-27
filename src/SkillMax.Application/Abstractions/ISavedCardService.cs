using SkillMax.Application.Models;

namespace SkillMax.Application.Abstractions;

public interface ISavedCardService
{
    /// <summary>Save or unsave a page. Returns the new saved state.</summary>
    Task<bool> ToggleAsync(int bookId, int pageIndex, CancellationToken ct = default);

    Task<bool> IsSavedAsync(int bookId, int pageIndex, CancellationToken ct = default);

    Task<IReadOnlyList<FeedCard>> GetSavedAsync(CancellationToken ct = default);
}
