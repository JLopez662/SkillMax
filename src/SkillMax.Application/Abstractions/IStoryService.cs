using SkillMax.Application.Models;

namespace SkillMax.Application.Abstractions;

/// <summary>
/// The stories rail above the feed: one bubble per followed channel, each opening on a short drawn
/// at random from that channel alone. The feed pools every source into one hat
/// (<see cref="IShortsService"/>); a story is the opposite — it asks one channel at a time.
/// </summary>
public interface IStoryService
{
    /// <summary>
    /// The bubbles, least recently opened first. Database only, no network, so the rail renders
    /// before any channel listing has been fetched. Empty when nothing is followed.
    /// <para>
    /// Takes the feed's focus so the rail can't contradict the scroll beneath it: a feed set to
    /// Coding showing Looksmaxx bubbles would be the app arguing with itself.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ChannelStory>> GetStoriesAsync(int? categoryId = null, CancellationToken ct = default);

    /// <summary>
    /// The same stories with <see cref="ChannelStory.Available"/> filled in from the cached
    /// listings. Called after the rail is already drawn, because on a cold start this is where the
    /// fetching happens.
    /// </summary>
    Task<IReadOnlyList<ChannelStory>> CountAvailableAsync(
        IReadOnlyList<ChannelStory> stories, CancellationToken ct = default);

    /// <summary>
    /// Up to <paramref name="count"/> shorts drawn at random from one channel, weighted the way
    /// the feed weights its own draw — an understood short still comes round, just less often.
    /// Video ids in <paramref name="exclude"/> are left out, which is how a re-roll asks for one
    /// it hasn't already dealt. Empty when the channel can't be reached and nothing is cached.
    /// </summary>
    Task<IReadOnlyList<ShortCard>> DrawAsync(
        string channelId,
        int count,
        IReadOnlyCollection<string> exclude,
        CancellationToken ct = default);

    /// <summary>Remember that this channel has been round, so its ring goes quiet until tomorrow.</summary>
    Task MarkSeenAsync(string channelId, CancellationToken ct = default);
}
