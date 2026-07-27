namespace SkillMax.Application.Models;

/// <summary>
/// A channel named one of the ways a person actually has one to hand: a handle, or any of the
/// URLs YouTube hands out. Exactly one of the two properties is set.
/// </summary>
public sealed record ChannelQuery
{
    private ChannelQuery() { }

    /// <summary>Handle without the '@'.</summary>
    public string? Handle { get; private init; }

    /// <summary>A UC… channel id.</summary>
    public string? ChannelId { get; private init; }

    /// <summary>A video or short — meaning "the channel that posted this", which is how someone
    /// who just watched something usually has the channel to hand.</summary>
    public string? VideoId { get; private init; }

    public static ChannelQuery FromHandle(string handle) => new() { Handle = handle };
    public static ChannelQuery FromChannelId(string channelId) => new() { ChannelId = channelId };
    public static ChannelQuery FromVideo(string videoId) => new() { VideoId = videoId };

    /// <summary>What to show while the channel is still being looked up.</summary>
    public string Label =>
        Handle is not null ? "@" + Handle
        : ChannelId is not null ? ChannelId
        : "that video's channel";
}
