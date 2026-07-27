namespace SkillMax.Maui.Components.Shared;

/// <summary>
/// The tile a channel without a picture falls back to. Shared by the rail and the viewer so the
/// same channel can't turn up as two different colours in the two places it appears.
/// </summary>
internal static class ChannelTint
{
    /// <param name="hue">From <see cref="Application.Models.ChannelStory.Hue"/>.</param>
    public static string Gradient(int hue) =>
        $"linear-gradient(150deg, hsl({hue} 72% 52%), hsl({(hue + 40) % 360} 55% 14%))";
}
