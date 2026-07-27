namespace SkillMax.Application.Abstractions;

/// <summary>
/// Hands a composed prompt over to the chat app. Implemented by the platform head — reaching another
/// installed app is an OS matter, and Infrastructure stays platform-free.
/// </summary>
public interface IAiHandoffService
{
    Task<AiHandoff> SendAsync(string text, CancellationToken ct = default);
}

/// <summary>How far the handoff got. The clipboard is written in every case but <see cref="Failed"/>.</summary>
public enum AiHandoff
{
    /// <summary>The chat app opened with the text already in the composer. Nothing to paste.</summary>
    Prefilled,

    /// <summary>The chat app opened, but on whatever it was showing. The reader pastes.</summary>
    Opened,

    /// <summary>The app isn't installed; the web version opened instead and the reader pastes.</summary>
    Web,

    /// <summary>Nothing opened, but the text is on the clipboard.</summary>
    Copied,

    Failed,
}
