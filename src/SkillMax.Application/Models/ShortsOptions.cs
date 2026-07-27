namespace SkillMax.Application.Models;

/// <summary>
/// How shorts behave in the feed. Which channels they come from is the user's business now —
/// that list lives in the database and is edited in the Library.
/// </summary>
public sealed record ShortsOptions
{
    /// <summary>How long a fetched channel listing stays usable before it's refreshed.</summary>
    public TimeSpan CacheFor { get; init; } = TimeSpan.FromHours(12);

    /// <summary>
    /// The same for a search. Shorter, because a search is a moving target where a channel's
    /// uploads are not: new videos from anyone can enter the results at any time.
    /// </summary>
    public TimeSpan SearchCacheFor { get; init; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Drop a channel's regular landscape uploads. Costs one cheap request per newly seen video
    /// the first time a channel is fetched; the verdict is cached with the listing.
    /// <para>
    /// Turning this back on doesn't retroactively sort a listing that was fetched with it off —
    /// those videos are cached as "not asked", and unasked is treated as not-a-short. They're
    /// classified at the next refresh (see <see cref="CacheFor"/>), not before.
    /// </para>
    /// </summary>
    public bool ShortsOnly { get; init; } = true;

    /// <summary>How many pages ride between one short and the next.</summary>
    public int EveryNPosts { get; init; } = 3;

    /// <summary>
    /// How many shorts a story bubble deals from its channel before it runs out and the viewer
    /// moves on to the next channel. One would make the rail a single tap with nowhere to go;
    /// this is what gives tapping forward something to do inside a channel.
    /// </summary>
    public int StoryDeckSize { get; init; } = 3;

    /// <summary>
    /// A channel the app follows on first run, so a fresh install has something in the feed and
    /// an example in the Library. Written once (guarded by a setting), then it's just another
    /// row the user can unfollow. Null ships an empty list instead.
    /// </summary>
    public SeedChannel? Seed { get; init; }
}

/// <param name="ChannelId">Known up front so first run costs no network.</param>
public sealed record SeedChannel(string Handle, string ChannelId, string Title);
