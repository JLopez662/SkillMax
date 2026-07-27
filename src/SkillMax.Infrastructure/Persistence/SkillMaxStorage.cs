namespace SkillMax.Infrastructure.Persistence;

/// <summary>
/// App-storage locations, provided by the platform head (MAUI) at startup so the
/// platform-free Infrastructure layer never calls MAUI APIs directly. Directories
/// are created on construction.
/// </summary>
public sealed class SkillMaxStorage
{
    public SkillMaxStorage(string appDataDir)
    {
        BooksDir = Path.Combine(appDataDir, "books");
        PageCacheDir = Path.Combine(appDataDir, "pagecache");
        TextCacheDir = Path.Combine(appDataDir, "textcache");
        ShortsCacheDir = Path.Combine(appDataDir, "shortscache");
        Directory.CreateDirectory(BooksDir);
        Directory.CreateDirectory(PageCacheDir);
        Directory.CreateDirectory(TextCacheDir);
        Directory.CreateDirectory(ShortsCacheDir);
    }

    /// <summary>Where imported PDF copies live.</summary>
    public string BooksDir { get; }

    /// <summary>Where rendered page PNGs are cached.</summary>
    public string PageCacheDir { get; }

    /// <summary>Where extracted page text blocks are cached, one file per page.</summary>
    public string TextCacheDir { get; }

    /// <summary>Where a followed channel's shorts listing is cached, one file per channel.</summary>
    public string ShortsCacheDir { get; }
}
