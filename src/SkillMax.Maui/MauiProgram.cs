using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Maui.Services;
using SkillMax.Infrastructure.DependencyInjection;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Maui;

public static class MauiProgram
{
    /// <summary>
    /// How shorts behave in the feed. Which channels they come from is edited in the Library;
    /// the seed below is only what a fresh install starts out following, so the feed isn't empty
    /// before anyone has added anything.
    /// </summary>
    private static readonly ShortsOptions Shorts = new()
    {
        EveryNPosts = 3,
        Seed = new SeedChannel(
            Handle: "youarelovedtemplates",
            ChannelId: "UCP6JyzIdju0DCE3tuswOjeA",
            Title: "You Are Loved Templates"),
    };

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "skillmax.db");
        builder.Services.AddSkillMaxInfrastructure(dbPath, FileSystem.AppDataDirectory, Shorts);

        // Reaching another installed app is an OS matter, so it's registered here rather than in
        // Infrastructure — which stays platform-free.
        builder.Services.AddSingleton<IAiHandoffService, ChatGptHandoffService>();

#if ANDROID
        // A short starts playing when the post is tapped — but that tap lands on our poster, not
        // inside YouTube's iframe, and the WebView would otherwise refuse to start the video
        // because the gesture wasn't its own. Embedded shorts are the only media in the app.
        Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler.BlazorWebViewMapper
            .AppendToMapping("SkillMaxInlineMedia", (handler, _) =>
            {
                if (handler.PlatformView.Settings is { } settings)
                    settings.MediaPlaybackRequiresUserGesture = false;
            });
#endif

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // Create the DB off the startup critical path; the Blazor UI (MainLayout) awaits the
        // same task before rendering data, so nothing queries the DB before it exists.
        _ = app.Services.GetRequiredService<DatabaseInitializer>().EnsureInitializedAsync();

        return app;
    }
}
