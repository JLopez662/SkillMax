namespace SkillMax.Application.Models;

/// <summary>
/// One YouTube Short from a followed channel, shown in the feed as an embedded post.
/// Nothing is downloaded: the card only carries the ids the UI needs to build a poster
/// frame and, once the post is tapped, a player URL.
/// </summary>
public sealed record ShortCard
{
    /// <summary>11-character YouTube video id.</summary>
    public required string VideoId { get; init; }

    public required string Title { get; init; }

    /// <summary>Channel display name, e.g. "You Are Loved Templates".</summary>
    public required string ChannelTitle { get; init; }

    /// <summary>Channel handle without the '@'. Empty when a search turned this up and YouTube
    /// didn't say who posted it.</summary>
    public required string ChannelHandle { get; init; }

    /// <summary>
    /// The search that found this short, when it came from one. Null means it came from a
    /// followed channel, which the card says instead.
    /// </summary>
    public string? FoundVia { get; init; }

    /// <summary>The viewer has marked this short understood, so it's drawn less often.</summary>
    public bool Understood { get; init; }

    public DateTimeOffset? Published { get; init; }

    /// <summary>
    /// Poster frame at the video's own aspect — 576x1024 or 720x1280 for a short, i.e. the frame
    /// as shot. This is the one YouTube generates for vertical video; the "oardefault" name that
    /// turns up in older write-ups 404s. Not guaranteed, hence <see cref="PosterFallbackUrl"/>.
    /// </summary>
    public string PosterUrl => $"https://i.ytimg.com/vi/{VideoId}/oar2.jpg";

    /// <summary>Present for every video ever uploaded, but 4:3 with the short pillarboxed inside
    /// it — the CSS crops to the card, so a fallback poster is tighter than the real one.</summary>
    public string PosterFallbackUrl => $"https://i.ytimg.com/vi/{VideoId}/hqdefault.jpg";

    /// <summary>
    /// The player, loaded only after a tap. The nocookie host keeps the WebView from picking up
    /// tracking cookies; <c>playsinline</c> keeps the video in the card instead of handing it to
    /// the system player; <c>fs=0</c> hides the fullscreen button, which the WebView has no
    /// handler for and would otherwise do nothing; <c>enablejsapi</c> lets the card hear that a
    /// video refused to play here (see smShorts in cards.js) instead of showing a dead frame.
    /// </summary>
    public string EmbedUrl =>
        $"https://www.youtube-nocookie.com/embed/{VideoId}" +
        "?autoplay=1&playsinline=1&rel=0&modestbranding=1&fs=0&enablejsapi=1";

    /// <summary>Where "Open in YouTube" goes when a video refuses to embed.</summary>
    public string WatchUrl => $"https://www.youtube.com/shorts/{VideoId}";
}
