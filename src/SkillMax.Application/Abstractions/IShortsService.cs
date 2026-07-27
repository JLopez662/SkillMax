using SkillMax.Application.Models;

namespace SkillMax.Application.Abstractions;

public interface IShortsService
{
    /// <summary>
    /// A random handful of shorts from the followed channel. Returns an empty list when the
    /// channel can't be reached and nothing is cached — the feed then shows pages only.
    /// </summary>
    Task<IReadOnlyList<ShortCard>> GetShortsAsync(int count, int? categoryId = null, CancellationToken ct = default);
}
