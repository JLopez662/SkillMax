namespace SkillMax.Domain.Entities;

/// <summary>
/// A YouTube search the user follows. Its shorts ride in the feed the same way a channel's do —
/// the difference is that a search keeps finding new creators, where a channel is one voice.
/// </summary>
public class SearchTerm
{
    public int Id { get; set; }

    /// <summary>What the user typed, kept as typed — it's what the feed shows as the source.</summary>
    public string Query { get; set; } = "";

    /// <summary>Trimmed, collapsed and lower-cased; the key for "already following this search".</summary>
    public string Normalized { get; set; } = "";

    public DateTime AddedUtc { get; set; }

    /// <summary>The shelf label this search wears, or null for Uncategorised.</summary>
    public int? CategoryId { get; set; }

    /// <summary>Tidies the spacing without touching the words or their casing.</summary>
    public static string Collapse(string? query) =>
        string.Join(' ', (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Whitespace and casing shouldn't make two searches look like different ones.</summary>
    public static string Normalize(string? query) => Collapse(query).ToLowerInvariant();
}
