using System.Text.RegularExpressions;
using SkillMax.Application.Models;

namespace SkillMax.Application;

/// <summary>
/// Turns whatever a person pastes into the "follow a channel" box into something lookup-able.
/// Handles the shapes YouTube actually produces — the channel URL, the shorts or videos tab, a
/// share link with tracking query, a bare handle — rather than demanding one canonical form.
/// A link to a video or a short counts too: the channel that posted it is almost always the one
/// meant, and having just watched something is the usual reason to want to follow it.
/// </summary>
public static partial class ChannelInput
{
    /// <summary>Null when the text isn't a channel reference at all.</summary>
    public static ChannelQuery? Parse(string? text)
    {
        var input = (text ?? "").Trim();
        if (input.Length == 0) return null;

        input = SchemeRegex().Replace(input, "");

        // youtu.be/<id> is only ever a video, and its host has to be read before it's stripped.
        var shortLink = ShortLinkRegex().Match(input);
        if (shortLink.Success) return ChannelQuery.FromVideo(shortLink.Groups[1].Value);

        input = HostRegex().Replace(input, "").TrimStart('/');

        // /watch?v=<id> keeps the id in the query, so it's read before the query is dropped.
        var watch = WatchRegex().Match(input);
        if (watch.Success) return ChannelQuery.FromVideo(watch.Groups[1].Value);

        // Everything past here is path-only: share links carry ?si=… and the like.
        var cut = input.IndexOfAny(['?', '#']);
        if (cut >= 0) input = input[..cut];

        // /shorts/<id>, /live/<id>, /embed/<id>
        var byVideo = VideoPathRegex().Match(input);
        if (byVideo.Success) return ChannelQuery.FromVideo(byVideo.Groups[1].Value);

        // /channel/UC… — the id is already in hand.
        var byId = ChannelIdPathRegex().Match(input);
        if (byId.Success) return ChannelQuery.FromChannelId(byId.Groups[1].Value);

        // A bare UC… id, pasted on its own.
        if (BareChannelIdRegex().IsMatch(input)) return ChannelQuery.FromChannelId(input);

        // Anything else: the first path segment is the handle, minus the '@' and minus whichever
        // tab follows it (/shorts, /videos, /featured…).
        var handle = input.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (handle is null) return null;

        handle = handle.TrimStart('@');
        return HandleRegex().IsMatch(handle) ? ChannelQuery.FromHandle(handle) : null;
    }

    [GeneratedRegex(@"^\s*[a-zA-Z][a-zA-Z0-9+.-]*://")]
    private static partial Regex SchemeRegex();

    [GeneratedRegex(@"^(?:www\.)?youtu\.be/([\w-]{11})", RegexOptions.IgnoreCase)]
    private static partial Regex ShortLinkRegex();

    [GeneratedRegex(@"^(?:www\.|m\.|music\.)?youtube\.com", RegexOptions.IgnoreCase)]
    private static partial Regex HostRegex();

    [GeneratedRegex(@"^watch\?(?:.*&)?v=([\w-]{11})", RegexOptions.IgnoreCase)]
    private static partial Regex WatchRegex();

    [GeneratedRegex(@"^(?:shorts|live|embed|v)/([\w-]{11})", RegexOptions.IgnoreCase)]
    private static partial Regex VideoPathRegex();

    [GeneratedRegex(@"^channel/(UC[\w-]{22})")]
    private static partial Regex ChannelIdPathRegex();

    [GeneratedRegex(@"^UC[\w-]{22}$")]
    private static partial Regex BareChannelIdRegex();

    // YouTube handles: 3–30 of letters, digits, underscore, hyphen, period.
    [GeneratedRegex(@"^[A-Za-z0-9_.-]{3,30}$")]
    private static partial Regex HandleRegex();
}
