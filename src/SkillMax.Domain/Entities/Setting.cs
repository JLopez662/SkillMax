namespace SkillMax.Domain.Entities;

/// <summary>Key/value app settings (feed source mode, active book, streak, …).</summary>
public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
