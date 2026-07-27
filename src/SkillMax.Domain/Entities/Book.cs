namespace SkillMax.Domain.Entities;

/// <summary>An imported PDF the user studies. Pages from it become feed posts.</summary>
public class Book
{
    public int Id { get; set; }

    public string Title { get; set; } = "";
    public string? Author { get; set; }

    /// <summary>Absolute path to the imported PDF copy in app storage.</summary>
    public string FilePath { get; set; } = "";

    public int PageCount { get; set; }

    /// <summary>Rough count of pages served, capped at <see cref="PageCount"/>. Drives the % ring.</summary>
    public int PagesSeen { get; set; }

    /// <summary>Cover accent colour (hex), assigned at import for the gradient tile.</summary>
    public string ColorHex { get; set; } = "#4f5bd5";

    public DateTime ImportedUtc { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>The shelf label this book wears, or null for Uncategorised.</summary>
    public int? CategoryId { get; set; }
}
