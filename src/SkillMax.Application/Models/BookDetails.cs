namespace SkillMax.Application.Models;

/// <summary>
/// What a book looks like it should be called. A suggestion, never applied on its own — the
/// rename sheet fills its fields with this and the reader decides.
/// </summary>
public sealed record BookDetails(string Title, string? Author);
