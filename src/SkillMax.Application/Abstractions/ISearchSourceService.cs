using SkillMax.Application.Models;
using SkillMax.Domain.Entities;

namespace SkillMax.Application.Abstractions;

public interface ISearchSourceService
{
    /// <summary>The searches the user follows, oldest first.</summary>
    Task<IReadOnlyList<SearchTerm>> GetSearchesAsync(CancellationToken ct = default);

    /// <summary>
    /// Run the search once before storing it, so "no shorts for that" is answered while the user
    /// is still looking at the box rather than by a feed that never shows anything.
    /// </summary>
    Task<SearchAddResult> AddAsync(string query, CancellationToken ct = default);

    Task RemoveAsync(int id, CancellationToken ct = default);
}
