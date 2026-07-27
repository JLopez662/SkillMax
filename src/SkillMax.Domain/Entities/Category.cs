namespace SkillMax.Domain.Entities;

/// <summary>
/// A shelf label the reader invents — "Coding", "AI", "Looksmaxx" — worn by books and by the
/// YouTube sources that feed the same subject. One taxonomy over both, so a channel and a book
/// about the same thing sort together.
/// </summary>
public class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Chip colour (hex), taken from the same palette import assigns covers from.</summary>
    public string ColorHex { get; set; } = "#4f5bd5";

    /// <summary>Position in the chip rail. Ties break on <see cref="Id"/>.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedUtc { get; set; }
}
