using SkillMax.Application.Abstractions;

#if ANDROID
using Android.Content;
#endif

namespace SkillMax.Maui.Services;

/// <summary>
/// Hands a page's prompt to ChatGPT, best path first:
///
///   1. A send intent aimed straight at ChatGPT's package. It registers a share target for
///      text/plain, so this opens a new chat with the prompt already in the composer.
///   2. Its plain launch intent, if the share target isn't there. The reader pastes.
///   3. The web version, if the app isn't installed at all.
///
/// The clipboard is written before any of them, so a paste is always available even when the
/// handoff lands somewhere other than a fresh chat.
/// </summary>
public sealed class ChatGptHandoffService : IAiHandoffService
{
    private const string AndroidPackage = "com.openai.chatgpt";
    private const string WebUrl = "https://chatgpt.com/";

    public Task<AiHandoff> SendAsync(string text, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(text)
            ? Task.FromResult(AiHandoff.Failed)
            : MainThread.InvokeOnMainThreadAsync(() => SendCoreAsync(text));

    private static async Task<AiHandoff> SendCoreAsync(string text)
    {
        var copied = false;
        try
        {
            await Clipboard.Default.SetTextAsync(text);
            copied = true;
        }
        catch { /* an unavailable clipboard shouldn't stop the app from opening */ }

#if ANDROID
        if (TryShare(text)) return AiHandoff.Prefilled;
        if (TryLaunch()) return copied ? AiHandoff.Opened : AiHandoff.Failed;
#endif

        try
        {
            await Browser.Default.OpenAsync(WebUrl, BrowserLaunchMode.External);
            return copied ? AiHandoff.Web : AiHandoff.Failed;
        }
        catch
        {
            return copied ? AiHandoff.Copied : AiHandoff.Failed;
        }
    }

#if ANDROID
    /// <summary>
    /// Needs the &lt;queries&gt; entry in AndroidManifest.xml: from Android 11 the package is
    /// invisible without it and the resolve below always misses, silently downgrading every
    /// handoff to a paste.
    /// </summary>
    private static bool TryShare(string text)
    {
        try
        {
            var intent = new Intent(Intent.ActionSend);
            intent.SetType("text/plain");
            intent.PutExtra(Intent.ExtraText, text);
            intent.SetPackage(AndroidPackage);
            return Start(intent);
        }
        catch { return false; }
    }

    private static bool TryLaunch()
    {
        try
        {
            var intent = Platform.AppContext.PackageManager?.GetLaunchIntentForPackage(AndroidPackage);
            return intent is not null && Start(intent);
        }
        catch { return false; }
    }

    private static bool Start(Intent intent)
    {
        var pm = Platform.AppContext.PackageManager;
        if (pm is null || intent.ResolveActivity(pm) is null) return false;

        // Starting from the activity keeps the back stack sane — back out of ChatGPT and the feed
        // is still there. Only a start from the application context needs its own task.
        if (Platform.CurrentActivity is { } activity)
        {
            activity.StartActivity(intent);
            return true;
        }

        intent.AddFlags(ActivityFlags.NewTask);
        Platform.AppContext.StartActivity(intent);
        return true;
    }
#endif
}
