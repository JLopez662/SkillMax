using SkillMax.Domain.Entities;

namespace SkillMax.Application.Models;

public enum ChannelAddOutcome
{
    Added,

    /// <summary>The text wasn't a channel handle or URL.</summary>
    NotUnderstood,

    /// <summary>YouTube has no such channel.</summary>
    NotFound,

    /// <summary>Already in the list — matched on channel id, whichever way it was named.</summary>
    AlreadyFollowed,

    /// <summary>Couldn't reach YouTube to check.</summary>
    Unreachable,
}

/// <param name="Channel">The row that was added, or the existing one when already followed.</param>
public sealed record ChannelAddResult(ChannelAddOutcome Outcome, Channel? Channel = null)
{
    public bool Ok => Outcome is ChannelAddOutcome.Added;

    /// <summary>What to put under the box when it didn't work.</summary>
    public string? Message => Outcome switch
    {
        ChannelAddOutcome.Added => null,
        ChannelAddOutcome.AlreadyFollowed => $"You already follow {Channel?.Title ?? "that channel"}.",
        ChannelAddOutcome.NotUnderstood => "That doesn't look like a channel. Try @handle or a youtube.com channel link.",
        ChannelAddOutcome.NotFound => "YouTube has no channel by that name.",
        _ => "Couldn't reach YouTube. Check your connection and try again.",
    };
}
