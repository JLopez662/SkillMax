namespace SkillMax.Domain.Entities;

/// <summary>
/// A short the viewer has marked as understood. Keyed by video id rather than by source: the same
/// short can arrive from a followed channel and from a followed search, and having watched it once
/// is having watched it, whichever way it turned up.
/// </summary>
public class UnderstoodShort
{
    public int Id { get; set; }

    /// <summary>11-character YouTube video id.</summary>
    public string VideoId { get; set; } = "";

    public DateTime MarkedUtc { get; set; }
}
