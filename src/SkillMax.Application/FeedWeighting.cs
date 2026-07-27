namespace SkillMax.Application;

/// <summary>
/// How the feed decides what comes round again.
/// <para>
/// Marking a post understood doesn't retire it — spaced repetition is the whole point of a study
/// feed, and a page you understood today is one you'll want again next month. It drops the post's
/// weight in the draw instead, so the material that hasn't landed yet gets most of the airtime and
/// the rest keeps trickling through.
/// </para>
/// </summary>
public static class FeedWeighting
{
    /// <summary>
    /// What an understood post is worth against one that isn't: roughly one appearance for every
    /// seven. Low enough to feel like progress, not zero — nothing is ever locked out of the feed.
    /// </summary>
    public const double UnderstoodWeight = 0.15;

    public static double WeightFor(bool understood) => understood ? UnderstoodWeight : 1.0;

    /// <summary>
    /// Draws up to <paramref name="count"/> distinct items, each with a chance proportional to its
    /// weight — weighted sampling without replacement. When everything left is understood their
    /// weights are equal again, so the feed still fills rather than running dry.
    /// </summary>
    public static List<T> Draw<T>(IEnumerable<T> candidates, Func<T, double> weight, int count, Random rng)
    {
        var pool = candidates.ToList();
        var weights = pool.Select(item => Math.Max(0.0001, weight(item))).ToList();
        var picked = new List<T>(Math.Min(count, pool.Count));

        while (picked.Count < count && pool.Count > 0)
        {
            var roll = rng.NextDouble() * weights.Sum();

            var i = 0;
            while (i < weights.Count - 1 && roll > weights[i])
            {
                roll -= weights[i];
                i++;
            }

            picked.Add(pool[i]);
            pool.RemoveAt(i);
            weights.RemoveAt(i);
        }

        return picked;
    }
}
