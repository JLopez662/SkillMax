using Microsoft.EntityFrameworkCore;
using SkillMax.Application;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

/// <summary>
/// The shorts half of the feed: every followed channel's listing, pooled and shuffled.
/// Scoped, because the list of channels comes from the database; the fetching and caching
/// underneath belongs to the shared <see cref="YouTubeChannelCatalog"/>.
/// </summary>
public sealed class ShortsService : IShortsService
{
    private readonly AppDbContext _db;
    private readonly YouTubeChannelCatalog _catalog;

    public ShortsService(AppDbContext db, YouTubeChannelCatalog catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<ShortCard>> GetShortsAsync(
        int count, int? categoryId = null, CancellationToken ct = default)
    {
        if (count <= 0) return Array.Empty<ShortCard>();

        // Both DB reads finish before any HTTP starts — the scoped DbContext is shared and not
        // concurrency-safe, and the fetches below run in parallel.
        var channels = await _db.Channels
            .Where(c => categoryId == null || c.CategoryId == categoryId)
            .OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);

        var searches = await _db.SearchTerms
            .Where(s => categoryId == null || s.CategoryId == categoryId)
            .OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        if (channels.Count == 0 && searches.Count == 0) return Array.Empty<ShortCard>();

        var understood = (await _db.UnderstoodShorts.Select(u => u.VideoId).ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet();

        var fromChannels = Task.WhenAll(channels.Select(async channel =>
        {
            var videos = await _catalog.GetShortsAsync(channel.ChannelId, ct).ConfigureAwait(false);

            // A channel's own listing carries no per-video creator: it's the channel, by definition.
            return videos.Select(v => new ShortCard
            {
                VideoId = v.Id,
                Title = v.Title,
                ChannelTitle = channel.Title,
                ChannelHandle = channel.Handle,
                Understood = understood.Contains(v.Id),
                Published = v.Published,
            });
        }));

        var fromSearches = Task.WhenAll(searches.Select(async search =>
        {
            var videos = await _catalog.SearchShortsAsync(search.Query, ct).ConfigureAwait(false);

            // A search spans creators, so each hit is attributed to whoever posted it — and the
            // card also says which search surfaced it.
            return videos.Select(v => new ShortCard
            {
                VideoId = v.Id,
                Title = v.Title,
                ChannelTitle = v.ChannelTitle ?? "YouTube",
                ChannelHandle = v.ChannelHandle ?? "",
                FoundVia = search.Query,
                Understood = understood.Contains(v.Id),
                Published = v.Published,
            });
        }));

        await Task.WhenAll(fromChannels, fromSearches).ConfigureAwait(false);

        // Everything goes in the same hat, so a source with more shorts shows up more — the same
        // way a longer book supplies more pages. Deduped by video id: a short can be both on a
        // followed channel and in a followed search, and it shouldn't get two tickets.
        var pool = fromChannels.Result.SelectMany(x => x)
            .Concat(fromSearches.Result.SelectMany(x => x))
            .GroupBy(card => card.VideoId)
            .Select(group => group.First())
            .ToList();
        if (pool.Count == 0) return Array.Empty<ShortCard>();

        // Small enough to weight exactly, unlike the page draw: a short already understood keeps
        // its place in the pool at a fraction of the weight.
        return FeedWeighting.Draw(
            pool,
            card => FeedWeighting.WeightFor(card.Understood),
            count,
            Random.Shared);
    }
}
