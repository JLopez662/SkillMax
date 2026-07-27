using Microsoft.EntityFrameworkCore;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

public sealed class CategoryService : ICategoryService
{
    /// <summary>Settings key holding the feed's current focus. No row means everything.</summary>
    private const string FocusKey = "feed.category";

    /// <summary>
    /// Chip colours, cycled by position so a new category never lands on the one before it. The
    /// same palette import assigns book covers from, so a category never looks foreign.
    /// </summary>
    private static readonly string[] Palette =
    {
        "#4f5bd5", "#d62976", "#2b6a63", "#c0492f",
        "#8a5cc4", "#c78a24", "#2b8a8a", "#3f7b3a", "#d0417e",
    };

    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct = default) =>
        await _db.Categories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<int, CategoryUse>> GetUsageAsync(CancellationToken ct = default)
    {
        // Three counts rather than one grouped query: they're over different tables, and the
        // scoped DbContext has to run them one after another anyway.
        var books = await CountAsync(_db.Books.Where(b => b.CategoryId != null)
            .Select(b => b.CategoryId!.Value), ct).ConfigureAwait(false);

        var channels = await CountAsync(_db.Channels.Where(c => c.CategoryId != null)
            .Select(c => c.CategoryId!.Value), ct).ConfigureAwait(false);

        var searches = await CountAsync(_db.SearchTerms.Where(s => s.CategoryId != null)
            .Select(s => s.CategoryId!.Value), ct).ConfigureAwait(false);

        var ids = books.Keys.Concat(channels.Keys).Concat(searches.Keys).Distinct();

        return ids.ToDictionary(
            id => id,
            id => new CategoryUse(
                books.GetValueOrDefault(id),
                channels.GetValueOrDefault(id) + searches.GetValueOrDefault(id)));
    }

    private static async Task<Dictionary<int, int>> CountAsync(IQueryable<int> ids, CancellationToken ct) =>
        await ids.GroupBy(id => id)
                 .Select(g => new { Id = g.Key, Count = g.Count() })
                 .ToDictionaryAsync(x => x.Id, x => x.Count, ct)
                 .ConfigureAwait(false);

    public async Task<Category> AddAsync(string name, CancellationToken ct = default)
    {
        var clean = Tidy(name);
        if (clean.Length == 0) clean = "New category";

        // Client-side comparison: SQLite's default collation is case-sensitive, and adding a
        // NOCASE index would mean rebuilding the table on databases that predate categories.
        var all = await _db.Categories.ToListAsync(ct).ConfigureAwait(false);
        var existing = all.FirstOrDefault(c => string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var category = new Category
        {
            Name = clean,
            ColorHex = Palette[all.Count % Palette.Length],
            SortOrder = all.Count == 0 ? 0 : all.Max(c => c.SortOrder) + 1,
            CreatedUtc = DateTime.UtcNow,
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return category;
    }

    public async Task RenameAsync(int id, string name, CancellationToken ct = default)
    {
        var clean = Tidy(name);
        if (clean.Length == 0) return;   // a category with no name can't be picked out of a rail

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (category is null) return;

        category.Name = clean;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (category is null) return;

        // Done here rather than by a foreign key: see the note in AppDbContext — an upgraded
        // database has the column but not the constraint, so the constraint can't be relied on.
        foreach (var book in await _db.Books.Where(b => b.CategoryId == id).ToListAsync(ct).ConfigureAwait(false))
            book.CategoryId = null;

        foreach (var channel in await _db.Channels.Where(c => c.CategoryId == id).ToListAsync(ct).ConfigureAwait(false))
            channel.CategoryId = null;

        foreach (var search in await _db.SearchTerms.Where(s => s.CategoryId == id).ToListAsync(ct).ConfigureAwait(false))
            search.CategoryId = null;

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // A feed focused on a category that no longer exists would draw nothing at all.
        if (await GetFocusAsync(ct).ConfigureAwait(false) == id)
            await SetFocusAsync(null, ct).ConfigureAwait(false);
    }

    public async Task AssignBookAsync(int bookId, int? categoryId, CancellationToken ct = default)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct).ConfigureAwait(false);
        if (book is null) return;

        book.CategoryId = await ResolveAsync(categoryId, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task AssignChannelAsync(int channelId, int? categoryId, CancellationToken ct = default)
    {
        var channel = await _db.Channels.FirstOrDefaultAsync(c => c.Id == channelId, ct).ConfigureAwait(false);
        if (channel is null) return;

        channel.CategoryId = await ResolveAsync(categoryId, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task AssignSearchAsync(int searchId, int? categoryId, CancellationToken ct = default)
    {
        var search = await _db.SearchTerms.FirstOrDefaultAsync(s => s.Id == searchId, ct).ConfigureAwait(false);
        if (search is null) return;

        search.CategoryId = await ResolveAsync(categoryId, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Guards against filing something under a category that's since been deleted.</summary>
    private async Task<int?> ResolveAsync(int? categoryId, CancellationToken ct)
    {
        if (categoryId is not { } id) return null;
        return await _db.Categories.AnyAsync(c => c.Id == id, ct).ConfigureAwait(false) ? id : null;
    }

    // ---- feed focus ----

    public async Task<int?> GetFocusAsync(CancellationToken ct = default)
    {
        var row = await _db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == FocusKey, ct)
            .ConfigureAwait(false);

        if (row is null || !int.TryParse(row.Value, out var id)) return null;

        // A category deleted out from under the setting reads as "everything" rather than as a
        // feed that mysteriously has nothing in it.
        return await _db.Categories.AnyAsync(c => c.Id == id, ct).ConfigureAwait(false) ? id : null;
    }

    public async Task SetFocusAsync(int? categoryId, CancellationToken ct = default)
    {
        var row = await _db.Settings.FindAsync(new object[] { FocusKey }, ct).ConfigureAwait(false);

        if (categoryId is not { } id)
        {
            if (row is not null) _db.Settings.Remove(row);
        }
        else if (row is null)
        {
            _db.Settings.Add(new Setting { Key = FocusKey, Value = id.ToString() });
        }
        else
        {
            row.Value = id.ToString();
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string Tidy(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
