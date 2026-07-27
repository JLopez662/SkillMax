namespace SkillMax.Application.Models;

/// <summary>
/// A slide background. Each recipe carries its own ink, dimmed ink and hairline, so contrast is
/// decided once per background rather than hoped for wherever it lands.
/// </summary>
/// <param name="Id">Stable key, persisted-safe.</param>
/// <param name="Name">Display name (debug overlays, settings).</param>
/// <param name="Css">Value for the CSS <c>background</c> shorthand.</param>
/// <param name="Ink">Body text colour.</param>
/// <param name="Dim">Secondary text: captions, the page-source strip, "continued".</param>
/// <param name="Rule">Hairline colour for rules and figure frames.</param>
/// <param name="Grain">Opacity of the grain overlay, 0–1.</param>
public sealed record CardBackground(
    string Id,
    string Name,
    string Css,
    string Ink,
    string Dim,
    string Rule,
    double Grain);

/// <summary>
/// The fixed pool of slide backgrounds. Assignment is hashed from the book and page so a page keeps
/// its look between sessions, then nudged one step along when the post above drew the same recipe —
/// two identical cards in a row read as a bug rather than as randomness.
/// </summary>
public static class CardBackgrounds
{
    /// <summary>
    /// Every recipe is a dark ground with light ink — pale cards read as washed out next to the
    /// black feed chrome. Variety comes from hue and texture instead of from swapping polarity:
    /// warm leather, cool graphite, deep green, indigo mesh, ember glow, teal, plum, blueprint grid.
    /// </summary>
    public static readonly IReadOnlyList<CardBackground> Pool = new[]
    {
        new CardBackground("leather", "Leather",
            "linear-gradient(158deg,#2e2822,#171310)",
            "#f1e8db", "#a89680", "rgba(241,232,219,.2)", .12),

        new CardBackground("slate", "Slate",
            "linear-gradient(rgba(255,255,255,.045) 1px,transparent 1px) 0 0/100% 30px," +
            "linear-gradient(90deg,rgba(255,255,255,.045) 1px,transparent 1px) 0 0/30px 100%," +
            "linear-gradient(165deg,#232833,#12161d)",
            "#e8edf4", "#93a1b3", "rgba(232,237,244,.2)", .05),

        new CardBackground("dusk", "Dusk",
            "radial-gradient(115% 85% at 12% 6%,#4e40a8,transparent 60%)," +
            "radial-gradient(105% 80% at 92% 96%,#8f3d80,transparent 62%),#1a1730",
            "#f4f0ff", "#b4a8d8", "rgba(244,240,255,.22)", .06),

        // The glow is the one place in the pool where ink met its ground at only 6.4:1; darkened to
        // #6d3418 and the dim tone lifted, so the brightest corner still clears 8.6:1 / 4.5:1.
        new CardBackground("ember", "Ember",
            "radial-gradient(105% 78% at 82% -4%,#6d3418,transparent 64%)," +
            "radial-gradient(80% 60% at 6% 104%,#3d2a1c,transparent 60%),#18130f",
            "#f8efe4", "#cdaa8a", "rgba(248,239,228,.2)", .07),

        new CardBackground("fern", "Fern",
            "radial-gradient(110% 80% at 10% 0%,#24352a,transparent 62%),linear-gradient(150deg,#1b2a20,#0d1610)",
            "#e7f0e8", "#93ab98", "rgba(231,240,232,.2)", .07),

        new CardBackground("tide", "Tide",
            "radial-gradient(120% 90% at 100% 0%,#12525c,transparent 62%),linear-gradient(160deg,#0d3b45,#061c25)",
            "#e6f5f6", "#8fbcc0", "rgba(230,245,246,.2)", .05),

        new CardBackground("graphite", "Graphite",
            "linear-gradient(162deg,#2a2a30,#131316)",
            "#f0f0f3", "#9a9aa6", "rgba(240,240,243,.18)", .06),

        new CardBackground("plum", "Plum",
            "radial-gradient(100% 75% at 20% 100%,#5a2044,transparent 62%),linear-gradient(155deg,#3b1730,#170b16)",
            "#fbeef6", "#c297b3", "rgba(251,238,246,.2)", .06),
    };

    public static CardBackground ById(string? id) =>
        Pool.FirstOrDefault(b => b.Id == id) ?? Pool[0];

    /// <summary>
    /// Pick the background for a page. Pass the previous card's <see cref="CardBackground.Id"/> when
    /// building a feed so neighbours never match.
    /// </summary>
    public static CardBackground For(int bookId, int pageIndex, string? previousId = null)
    {
        var i = (int)(Hash($"{bookId}:{pageIndex}") % (uint)Pool.Count);
        if (Pool[i].Id == previousId) i = (i + 1) % Pool.Count;
        return Pool[i];
    }

    /// <summary>FNV-1a, so the same page draws the same background on every device and run.</summary>
    private static uint Hash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s)
        {
            h ^= c;
            h *= 16777619u;
        }
        return h;
    }
}
