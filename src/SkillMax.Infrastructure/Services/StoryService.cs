using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SkillMax.Application;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

/// <summary>
/// The stories rail. Same catalog and the same weighting the feed uses — what differs is the
/// shape of the draw: a story asks one channel, where <see cref="ShortsService"/> pools them all.
/// Scoped, because the followed list and the seen marks both come from the database.
/// </summary>
public sealed class StoryService : IStoryService
{
    /// <summary>
    /// Settings key holding when a channel's story was last opened, as a round-trip timestamp.
    /// A row is left behind when a channel is unfollowed; it's inert, because only the keys of
    /// channels that still exist are ever read.
    /// </summary>
    private const string SeenPrefix = "story-seen:";

    private readonly AppDbContext _db;
    private readonly YouTubeChannelCatalog _catalog;

    public StoryService(AppDbContext db, YouTubeChannelCatalog catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<ChannelStory>> GetStoriesAsync(
        int? categoryId = null, CancellationToken ct = default)
    {
        var channels = await _db.Channels
            .Where(c => categoryId == null || c.CategoryId == categoryId)
            .OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        if (channels.Count == 0) return Array.Empty<ChannelStory>();

        var keys = channels.Select(c => SeenPrefix + c.ChannelId).ToList();
        var marks = await _db.Settings
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct)
            .ConfigureAwait(false);

        var today = DateTimeOffset.Now.Date;

        return channels
            .Select(channel =>
            {
                var opened = LastOpened(marks, channel.ChannelId);
                return (Channel: channel, Opened: opened, Story: new ChannelStory
                {
                    ChannelId = channel.ChannelId,
                    Handle = channel.Handle,
                    Title = channel.Title,
                    AvatarUrl = channel.AvatarUrl,
                    Seen = opened is { } when && when.ToLocalTime().Date == today,
                });
            })
            // Least recently opened first, so the rail reorders itself instead of freezing into
            // the order the channels happened to be added in. Never opened sorts oldest of all.
            .OrderBy(x => x.Opened ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Channel.Id)
            .Select(x => x.Story)
            .ToList();
    }

    public async Task<IReadOnlyList<ChannelStory>> CountAvailableAsync(
        IReadOnlyList<ChannelStory> stories, CancellationToken ct = default)
    {
        if (stories.Count == 0) return stories;

        // No database in here, which is what makes running them together safe: the scoped
        // DbContext isn't concurrency-safe and these fetches overlap.
        var counted = await Task.WhenAll(stories.Select(async story =>
        {
            var videos = await _catalog.GetShortsAsync(story.ChannelId, ct).ConfigureAwait(false);
            return story with { Available = videos.Count };
        })).ConfigureAwait(false);

        return counted;
    }

    public async Task<IReadOnlyList<ShortCard>> DrawAsync(
        string channelId,
        int count,
        IReadOnlyCollection<string> exclude,
        CancellationToken ct = default)
    {
        if (count <= 0) return Array.Empty<ShortCard>();

        var channel = await _db.Channels
            .FirstOrDefaultAsync(c => c.ChannelId == channelId, ct)
            .ConfigureAwait(false);
        if (channel is null) return Array.Empty<ShortCard>();

        // Read the database out before the fetch below, same reason as everywhere else here.
        var understood = (await _db.UnderstoodShorts
                .Select(u => u.VideoId)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToHashSet();

        var videos = await _catalog.GetShortsAsync(channelId, ct).ConfigureAwait(false);

        // A channel's own listing carries no per-video creator: it's the channel, by definition.
        var pool = videos
            .Where(video => !exclude.Contains(video.Id))
            .Select(video => new ShortCard
            {
                VideoId = video.Id,
                Title = video.Title,
                ChannelTitle = channel.Title,
                ChannelHandle = channel.Handle,
                Understood = understood.Contains(video.Id),
                Published = video.Published,
            })
            .ToList();
        if (pool.Count == 0) return Array.Empty<ShortCard>();

        return FeedWeighting.Draw(
            pool,
            card => FeedWeighting.WeightFor(card.Understood),
            count,
            Random.Shared);
    }

    public async Task MarkSeenAsync(string channelId, CancellationToken ct = default)
    {
        var key = SeenPrefix + channelId;
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        var row = await _db.Settings.FindAsync(new object[] { key }, ct).ConfigureAwait(false);
        if (row is null) _db.Settings.Add(new Setting { Key = key, Value = now });
        else row.Value = now;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static DateTimeOffset? LastOpened(IReadOnlyDictionary<string, string> marks, string channelId) =>
        marks.TryGetValue(SeenPrefix + channelId, out var value)
        && DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
            ? when
            : null;
}
