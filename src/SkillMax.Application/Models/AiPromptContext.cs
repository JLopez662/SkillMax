namespace SkillMax.Application.Models;

/// <summary>
/// The page a prompt is being built around. Everything the template can name lives here; anything
/// the book didn't give us (no author, no outline) comes through null and the optional parts of
/// the template that mention it are dropped — see <see cref="AiPromptTemplate"/>.
/// </summary>
public sealed record AiPromptContext
{
    public required string Book { get; init; }
    public string? Author { get; init; }

    /// <summary>The chapter the page falls in, when the book's outline could be read.</summary>
    public string? Chapter { get; init; }

    /// <summary>1-based, as printed on the card.</summary>
    public int Page { get; init; }
    public int PageCount { get; init; }

    /// <summary>The page's whole text, unsplit — what the reader would have if they held the book.</summary>
    public required string Text { get; init; }
}
