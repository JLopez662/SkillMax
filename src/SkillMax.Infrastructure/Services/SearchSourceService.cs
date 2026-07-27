using Microsoft.EntityFrameworkCore;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

/// <summary>The followed-search list behind the Library's Sources tab.</summary>
public sealed class SearchSourceService : ISearchSourceService
{
    private const int MinimumQueryLength = 3;

    private readonly AppDbContext _db;
    private readonly YouTubeChannelCatalog _catalog;

    public SearchSourceService(AppDbContext db, YouTubeChannelCatalog catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<SearchTerm>> GetSearchesAsync(CancellationToken ct = default) =>
        await _db.SearchTerms.OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false);

    public async Task<SearchAddResult> AddAsync(string query, CancellationToken ct = default)
    {
        // Stored as typed apart from stray spacing — it's what the feed shows as the source.
        var typed = SearchTerm.Collapse(query);
        var normalized = SearchTerm.Normalize(typed);
        if (normalized.Length < MinimumQueryLength) return new SearchAddResult(SearchAddOutcome.TooShort);

        var existing = await _db.SearchTerms
            .FirstOrDefaultAsync(s => s.Normalized == normalized, ct)
            .ConfigureAwait(false);
        if (existing is not null) return new SearchAddResult(SearchAddOutcome.AlreadyFollowed, existing);

        // Run it once now. A search that finds nothing is worth saying while the user is still
        // looking at the box, rather than storing a source that quietly never contributes.
        IReadOnlyList<CachedVideo> hits;
        try
        {
            hits = await _catalog.SearchShortsAsync(typed, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new SearchAddResult(SearchAddOutcome.Unreachable);
        }

        if (hits.Count == 0) return new SearchAddResult(SearchAddOutcome.NoShorts);

        var term = new SearchTerm
        {
            Query = typed,
            Normalized = normalized,
            AddedUtc = DateTime.UtcNow,
        };

        _db.SearchTerms.Add(term);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new SearchAddResult(SearchAddOutcome.Added, term, hits.Count);
    }

    public async Task RemoveAsync(int id, CancellationToken ct = default)
    {
        var term = await _db.SearchTerms.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (term is null) return;

        _db.SearchTerms.Remove(term);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _catalog.ForgetSearch(term.Query);
    }
}
