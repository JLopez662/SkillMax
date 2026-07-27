using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkillMax.Application.Models;
using SkillMax.Domain.Entities;

namespace SkillMax.Infrastructure.Persistence;

/// <summary>
/// Creates the database exactly once, off the app-startup critical path.
/// <para>
/// <see cref="EnsureInitializedAsync"/> starts the work on the thread pool on the first
/// call and caches the task, so every caller awaits the same single run. MauiProgram kicks
/// it off in the background (so the window/WebView/loading screen appear immediately) and the
/// Blazor UI awaits the same task before touching data.
/// </para>
/// <para>v1 uses <c>EnsureCreated</c> (schema-first). Swap to EF migrations once the model settles.</para>
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IServiceProvider _services;
    private readonly object _gate = new();
    private volatile Task? _initialization;

    public DatabaseInitializer(IServiceProvider services) => _services = services;

    public Task EnsureInitializedAsync()
    {
        if (_initialization is { } existing) return existing;
        lock (_gate)
        {
            return _initialization ??= Task.Run(InitializeAsync);
        }
    }

    private async Task InitializeAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        await CatchUpAsync(db).ConfigureAwait(false);
        await SeedChannelAsync(db, scope.ServiceProvider.GetRequiredService<ShortsOptions>())
            .ConfigureAwait(false);
    }

    /// <summary>
    /// EnsureCreated does nothing at all to a database that already exists, so a table added
    /// after someone's first install has to be created by hand. This is the seam where the first
    /// EF migration should go; until then, tables added later are listed here.
    /// </summary>
    private static async Task CatchUpAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "Channels" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Channels" PRIMARY KEY AUTOINCREMENT,
                "Handle" TEXT NOT NULL,
                "ChannelId" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "AvatarUrl" TEXT NULL,
                "AddedUtc" TEXT NOT NULL
            )
            """).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Channels_ChannelId" ON "Channels" ("ChannelId")
            """).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SearchTerms" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SearchTerms" PRIMARY KEY AUTOINCREMENT,
                "Query" TEXT NOT NULL,
                "Normalized" TEXT NOT NULL,
                "AddedUtc" TEXT NOT NULL
            )
            """).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SearchTerms_Normalized" ON "SearchTerms" ("Normalized")
            """).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "UnderstoodPages" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_UnderstoodPages" PRIMARY KEY AUTOINCREMENT,
                "BookId" INTEGER NOT NULL,
                "PageIndex" INTEGER NOT NULL,
                "MarkedUtc" TEXT NOT NULL,
                CONSTRAINT "FK_UnderstoodPages_Books_BookId" FOREIGN KEY ("BookId")
                    REFERENCES "Books" ("Id") ON DELETE CASCADE
            )
            """).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_UnderstoodPages_BookId_PageIndex"
                ON "UnderstoodPages" ("BookId", "PageIndex")
            """).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "UnderstoodShorts" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_UnderstoodShorts" PRIMARY KEY AUTOINCREMENT,
                "VideoId" TEXT NOT NULL,
                "MarkedUtc" TEXT NOT NULL
            )
            """).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_UnderstoodShorts_VideoId"
                ON "UnderstoodShorts" ("VideoId")
            """).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "Categories" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "ColorHex" TEXT NULL,
                "SortOrder" INTEGER NOT NULL,
                "CreatedUtc" TEXT NOT NULL
            )
            """).ConfigureAwait(false);

        // The three things a category can be put on. SQLite has no ADD COLUMN IF NOT EXISTS, so
        // each one is checked first — without this the second launch throws "duplicate column".
        await AddColumnAsync(db, "Books", "CategoryId").ConfigureAwait(false);
        await AddColumnAsync(db, "Channels", "CategoryId").ConfigureAwait(false);
        await AddColumnAsync(db, "SearchTerms", "CategoryId").ConfigureAwait(false);

        // Spelled out rather than looped: a literal keeps EF's raw-SQL analyzer quiet, and there
        // are only ever going to be three of these.
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Books_CategoryId" ON "Books" ("CategoryId")""")
            .ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Channels_CategoryId" ON "Channels" ("CategoryId")""")
            .ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_SearchTerms_CategoryId" ON "SearchTerms" ("CategoryId")""")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a nullable integer column if the table hasn't got one already.
    /// </summary>
    /// <remarks>
    /// A table name can't be a SQL parameter, so this interpolates — which is what EF1002 is
    /// warning about. It's suppressed rather than worked around because both arguments are
    /// compile-time literals from <see cref="CatchUpAsync"/> directly above; nothing a reader
    /// types reaches this method, and there is no code path by which it could.
    /// </remarks>
    private static async Task AddColumnAsync(AppDbContext db, string table, string column)
    {
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection — see the remarks above.
        var present = await db.Database
            .SqlQueryRaw<int>($"""SELECT COUNT(*) AS "Value" FROM pragma_table_info('{table}') WHERE "name" = '{column}'""")
            .FirstAsync()
            .ConfigureAwait(false);

        if (present > 0) return;

        await db.Database
            .ExecuteSqlRawAsync($"""ALTER TABLE "{table}" ADD COLUMN "{column}" INTEGER NULL""")
            .ConfigureAwait(false);
#pragma warning restore EF1002
    }

    /// <summary>
    /// Follows the channel the app ships with, once ever. Guarded by a setting rather than by
    /// "is the list empty", so unfollowing it sticks instead of being undone on next launch.
    /// </summary>
    private static async Task SeedChannelAsync(AppDbContext db, ShortsOptions options)
    {
        if (options.Seed is not { } seed) return;

        const string key = "channels.seeded";
        if (await db.Settings.AnyAsync(s => s.Key == key).ConfigureAwait(false)) return;

        var already = await db.Channels
            .AnyAsync(c => c.ChannelId == seed.ChannelId).ConfigureAwait(false);
        if (!already)
        {
            db.Channels.Add(new Channel
            {
                Handle = seed.Handle,
                ChannelId = seed.ChannelId,
                Title = seed.Title,
                AddedUtc = DateTime.UtcNow,
            });
        }

        db.Settings.Add(new Setting { Key = key, Value = "1" });
        await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
