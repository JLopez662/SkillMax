using System.Text.RegularExpressions;

namespace SkillMax.Application.Models;

/// <summary>
/// A chapter located in a book: where it starts, where it ends, and which section of it a given page
/// falls in. Page numbers are 1-based (what the reader sees and what a PDF outline stores);
/// <see cref="StartIndex"/> and <see cref="EndIndex"/> are the 0-based indexes services take.
/// </summary>
public sealed record ChapterRef
{
    public required string Title { get; init; }

    /// <summary>The level-1 outline entry the page sits under, when the book has one.</summary>
    public string? Section { get; init; }

    public int StartPage { get; init; }
    public int EndPage { get; init; }

    public int StartIndex => StartPage - 1;
    public int EndIndex => EndPage - 1;
    public int PageCount => EndPage - StartPage + 1;

    /// <summary>
    /// The title without its "Chapter 20: " prefix, for use as a heading where the number is already
    /// shown as a label. Falls back to the full title when there's no prefix to strip.
    /// </summary>
    public string ShortTitle => Numbering.Replace(Title, "").Trim() is { Length: > 0 } s ? s : Title;

    /// <summary>"Chapter 20" on its own, or null when the title isn't numbered that way.</summary>
    public string? Label
    {
        get
        {
            var m = Numbering.Match(Title);
            return m.Success ? m.Value.TrimEnd(':', ' ', '.', '—', '-') : null;
        }
    }

    private static readonly Regex Numbering = new(
        @"^(chapter|part|section|law)\s+([0-9]+|[ivxlcdm]+)\s*[:.—-]?\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
