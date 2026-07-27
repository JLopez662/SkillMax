namespace SkillMax.Domain.Entities;

/// <summary>
/// A page the reader has marked as understood (the heart on the post). It stays in the rotation —
/// revision is the point of the feed — but it loses most of its weight in the draw, so pages that
/// haven't landed yet come round more often.
/// </summary>
public class UnderstoodPage
{
    public int Id { get; set; }

    public int BookId { get; set; }

    /// <summary>0-based page index, matching <c>FeedCard.PageIndex</c>.</summary>
    public int PageIndex { get; set; }

    public DateTime MarkedUtc { get; set; }

    public Book? Book { get; set; }
}
