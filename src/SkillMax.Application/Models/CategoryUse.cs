namespace SkillMax.Application.Models;

/// <summary>
/// How much is filed under a category. Shown on the settings row so a delete can be judged
/// before it happens.
/// </summary>
public sealed record CategoryUse(int Books, int Sources);
