using SkillMax.Application.Models;

namespace SkillMax.Application.Abstractions;

/// <summary>
/// The reader's prompt template — the words that travel with a page when it's handed to a chat
/// assistant. Stored as a setting so it survives an edit in Profile → ChatGPT prompt; absent means
/// <see cref="AiPromptTemplate.Default"/>, which is also what a reset restores.
/// </summary>
public interface IAiPromptService
{
    /// <summary>The saved template, or the default when the reader hasn't written one.</summary>
    Task<string> GetTemplateAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves an edited template. Blank, or the default verbatim, clears the setting instead — so
    /// a reader who resets goes back to tracking the default rather than pinning today's copy of it.
    /// </summary>
    Task SaveTemplateAsync(string? template, CancellationToken ct = default);

    /// <summary>The saved template filled in for one page: what the post will actually send.</summary>
    Task<string> ComposeAsync(AiPromptContext context, CancellationToken ct = default);
}
