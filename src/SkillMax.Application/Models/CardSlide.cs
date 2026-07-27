namespace SkillMax.Application.Models;

/// <summary>
/// A block as it appears on a slide. <see cref="Continued"/> marks text that carries on from the
/// previous slide, so a mid-thought start can be labelled instead of reading like a new one.
/// </summary>
public sealed record SlideBlock(PageBlockKind Kind, string Text, bool Continued = false);

/// <summary>
/// One card in a post's carousel. A post is always exactly one page; a page is one or more slides.
/// </summary>
public sealed record CardSlide
{
    public IReadOnlyList<SlideBlock> Blocks { get; init; } = Array.Empty<SlideBlock>();

    /// <summary>True when this slide shows the rendered page instead of text (figure or fallback).</summary>
    public bool IsRender { get; init; }

    /// <summary>Caption shown under a figure render, when the page gave us one.</summary>
    public string? Caption { get; init; }

    public int CharCount => Blocks.Sum(b => b.Text.Length);

    /// <summary>Starts mid-paragraph, so the slide gets a "continued" eyebrow.</summary>
    public bool StartsMidParagraph => Blocks.Count > 0 && Blocks[0].Continued;
}
