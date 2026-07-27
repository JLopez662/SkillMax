namespace SkillMax.Domain.Entities;

/// <summary>A YouTube channel the user follows. Its shorts ride in the feed between book pages.</summary>
public class Channel
{
    public int Id { get; set; }

    /// <summary>Channel handle without the '@', e.g. "youarelovedtemplates".</summary>
    public string Handle { get; set; } = "";

    /// <summary>The channel's UC… id — resolved when it was added, and the stable key ever after.</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>Display name, e.g. "You Are Loved Templates".</summary>
    public string Title { get; set; } = "";

    /// <summary>Channel picture, if the channel page offered one.</summary>
    public string? AvatarUrl { get; set; }

    public DateTime AddedUtc { get; set; }

    /// <summary>The shelf label this channel wears, or null for Uncategorised.</summary>
    public int? CategoryId { get; set; }
}
