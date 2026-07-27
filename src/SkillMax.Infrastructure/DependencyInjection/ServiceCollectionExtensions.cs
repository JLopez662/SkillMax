using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Infrastructure.Persistence;
using SkillMax.Infrastructure.Services;

namespace SkillMax.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSkillMaxInfrastructure(
        this IServiceCollection services, string sqliteFilePath, string appDataDir, ShortsOptions shorts)
    {
        services.AddDbContext<AppDbContext>(opts => opts.UseSqlite($"Data Source={sqliteFilePath}"));

        // DB creation, run once off the startup critical path.
        services.AddSingleton<DatabaseInitializer>();

        // Storage paths come from the platform head so Infrastructure stays platform-free.
        services.AddSingleton(new SkillMaxStorage(appDataDir));

        services.AddScoped<IBookService, BookService>();
        services.AddScoped<IPdfImportService, PdfImportService>();
        services.AddScoped<IFeedService, FeedService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IUnderstoodService, UnderstoodService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IAiPromptService, AiPromptService>();

        // Page rendering and text extraction are DB-free (the caller passes the PDF path) and
        // CPU/file-bound, so they're thread-safe singletons — never touching the shared scoped
        // DbContext. Both cache to disk, so a page is only processed once.
        services.AddSingleton<IPageImageService, PageImageService>();
        services.AddSingleton<IPageTextService, PageTextService>();
        services.AddSingleton<IChapterService, ChapterService>();
        services.AddSingleton<IBookMetadataService, BookMetadataService>();

        // Talking to YouTube — looking channels up, listing their shorts, caching both — is
        // HTTP and files with no DbContext in sight, so the catalog is a singleton shared by the
        // two scoped services that read the followed-channel list out of the database.
        services.AddSingleton(shorts);
        services.AddSingleton<YouTubeChannelCatalog>();
        services.AddScoped<IShortsService, ShortsService>();
        services.AddScoped<IStoryService, StoryService>();
        services.AddScoped<IChannelService, ChannelService>();
        services.AddScoped<ISearchSourceService, SearchSourceService>();

        return services;
    }
}
