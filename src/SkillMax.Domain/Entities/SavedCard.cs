namespace SkillMax.Domain.Entities;

/// <summary>A page the user bookmarked ("save to resurface"). One row per (Book, Page).</summary>
public class SavedCard
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>0-based page index within the book.</summary>
    public int PageIndex { get; set; }

    public DateTime SavedUtc { get; set; }
}
