using SkillMax.Application.Models;
using SkillMax.Domain.Entities;

namespace SkillMax.Application.Abstractions;

/// <summary>
/// The shelf labels, and what wears them. One category per book, channel or search — null means
/// Uncategorised, which is a place things can be found rather than a hole they fall down.
/// </summary>
public interface ICategoryService
{
    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct = default);

    /// <summary>How many books and sources sit under each category, keyed by category id.</summary>
    Task<IReadOnlyDictionary<int, CategoryUse>> GetUsageAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a category, or returns the existing one when the name is already taken — two
    /// categories called "AI" would be indistinguishable on a chip.
    /// </summary>
    Task<Category> AddAsync(string name, CancellationToken ct = default);

    Task RenameAsync(int id, string name, CancellationToken ct = default);

    /// <summary>
    /// Removes a category. Everything filed under it becomes Uncategorised — deleting a label
    /// has never meant deleting what it was stuck to.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    Task AssignBookAsync(int bookId, int? categoryId, CancellationToken ct = default);
    Task AssignChannelAsync(int channelId, int? categoryId, CancellationToken ct = default);
    Task AssignSearchAsync(int searchId, int? categoryId, CancellationToken ct = default);

    /// <summary>The category the feed is currently drawing from, or null for everything.</summary>
    Task<int?> GetFocusAsync(CancellationToken ct = default);

    Task SetFocusAsync(int? categoryId, CancellationToken ct = default);
}
