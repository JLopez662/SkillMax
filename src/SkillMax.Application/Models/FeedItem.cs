namespace SkillMax.Application.Models;

/// <summary>
/// One row in the feed. The feed used to be a list of book pages only; a row is now either a
/// page or a short from a followed channel, so the two can be interleaved without either
/// knowing the other exists. The private constructor keeps the set closed to these two.
/// </summary>
public abstract record FeedItem
{
    private FeedItem() { }

    public sealed record Page(FeedCard Card) : FeedItem;

    public sealed record Short(ShortCard Card) : FeedItem;
}
