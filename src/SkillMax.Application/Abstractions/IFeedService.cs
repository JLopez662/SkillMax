using SkillMax.Application.Models;

namespace SkillMax.Application.Abstractions;

public interface IFeedService
{
    /// <summary>
    /// Build a batch of random feed cards drawn from the user's books. Pass
    /// <paramref name="categoryId"/> to draw from one category only; null draws from everything.
    /// </summary>
    Task<IReadOnlyList<FeedCard>> GetFeedAsync(int count, int? categoryId = null, CancellationToken ct = default);
}
