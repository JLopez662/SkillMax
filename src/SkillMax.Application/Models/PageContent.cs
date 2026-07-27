namespace SkillMax.Application.Models;

public enum PageBlockKind
{
    /// <summary>A section heading — set larger, and never left alone at the end of a slide.</summary>
    Heading,
    Paragraph,
    /// <summary>A numbered or bulleted line. Cheap in characters, expensive in height.</summary>
    ListItem,
    /// <summary>A figure/exhibit that only exists as pixels; carries its caption as text.</summary>
    Figure,
}

/// <summary>One structural block of a page's text, as recovered by <c>IPageTextService</c>.</summary>
public sealed record PageBlock(PageBlockKind Kind, string Text);

/// <summary>
/// A page's text layer, ready to be split into slides. When <see cref="RequiresRender"/> is set
/// the page yielded no usable text (a scan, a full-page plate, a dense table) and the feed shows
/// the rendered page image instead.
/// </summary>
public sealed record PageContent
{
    public int PageIndex { get; init; }
    public IReadOnlyList<PageBlock> Blocks { get; init; } = Array.Empty<PageBlock>();
    public bool RequiresRender { get; init; }

    public static PageContent ImageOnly(int pageIndex) => new() { PageIndex = pageIndex, RequiresRender = true };
}
