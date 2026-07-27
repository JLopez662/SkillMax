namespace SkillMax.Application.Models;

/// <summary>
/// A chapter as it appears in a book's contents list: the chapter itself, plus the headings the
/// PDF outline records inside it. Only produced for books whose outline can be trusted — see
/// <see cref="Abstractions.IChapterService.GetChaptersAsync"/>.
/// </summary>
public sealed record BookChapter(ChapterRef Chapter, IReadOnlyList<ChapterSection> Sections);

/// <summary>A heading inside a chapter, and the 1-based page it starts on.</summary>
public sealed record ChapterSection(string Title, int Page);
