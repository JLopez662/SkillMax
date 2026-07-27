namespace SkillMax.Application.Models;

/// <summary>
/// One bubble in the stories rail: a followed channel, and whether its shorts have already been
/// round today. The shorts themselves aren't here — a deck is drawn when the bubble is tapped, so
/// the rail is on screen before anything touches the network.
/// </summary>
public sealed record ChannelStory
{
    /// <summary>The channel's UC… id, which is what the catalog is keyed by.</summary>
    public required string ChannelId { get; init; }

    /// <summary>Handle without the '@', e.g. "youarelovedtemplates".</summary>
    public required string Handle { get; init; }

    public required string Title { get; init; }

    /// <summary>Channel picture, if the channel page offered one. The rail falls back to an initial.</summary>
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// This channel has already been through today, so its ring goes quiet until tomorrow. The
    /// rail needs something to say each morning, and "you've seen it" is the only honest signal
    /// available — a channel's own uploads are far slower than a daily cycle.
    /// </summary>
    public required bool Seen { get; init; }

    /// <summary>
    /// How many shorts this channel currently has to offer, or null while nobody has counted yet.
    /// The rail paints its bubbles from the database first and fills the pips in afterwards, as
    /// the cached listings answer — so this is absent for the first moment, not zero.
    /// </summary>
    public int? Available { get; init; }

    /// <summary>
    /// The tint of the letter tile a channel without a picture falls back to, so two channels in
    /// a row can still be told apart. Arbitrary, but fixed for a given channel: hashed by hand
    /// because <see cref="string.GetHashCode()"/> is randomised per process and would repaint
    /// every avatar on each launch.
    /// </summary>
    public int Hue
    {
        get
        {
            var hash = 2166136261u;                                   // FNV-1a
            foreach (var c in ChannelId) hash = (hash ^ c) * 16777619u;
            return (int)(hash % 360);
        }
    }
}
