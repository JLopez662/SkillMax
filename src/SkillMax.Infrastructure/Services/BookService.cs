using Microsoft.EntityFrameworkCore;
using SkillMax.Application.Abstractions;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

public sealed class BookService : IBookService
{
    private readonly AppDbContext _db;
    private readonly SkillMaxStorage _storage;

    public BookService(AppDbContext db, SkillMaxStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<IReadOnlyList<Book>> GetBooksAsync(CancellationToken ct = default) =>
        await _db.Books.OrderByDescending(b => b.ImportedUtc)
                       .ToListAsync(ct).ConfigureAwait(false);

    public Task<Book?> GetBookAsync(int id, CancellationToken ct = default) =>
        _db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<int> GetBookCountAsync(CancellationToken ct = default) =>
        _db.Books.CountAsync(b => !b.IsArchived, ct);

    public async Task<int> GetPagesSeenTotalAsync(CancellationToken ct = default) =>
        await _db.Books.Where(b => !b.IsArchived)
                       .SumAsync(b => b.PagesSeen, ct).ConfigureAwait(false);

    public async Task SetEnabledAsync(int id, bool enabled, CancellationToken ct = default)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == id, ct).ConfigureAwait(false);
        if (book is null || book.IsArchived == !enabled) return;

        book.IsArchived = !enabled;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RenameAsync(int id, string title, string? author, CancellationToken ct = default)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == id, ct).ConfigureAwait(false);
        if (book is null) return;

        // Lengths match the column widths configured in AppDbContext, so a long paste is trimmed
        // here rather than thrown back by SQLite.
        var clean = (title ?? "").Trim();
        if (clean.Length == 0) return;
        book.Title = clean.Length > 300 ? clean[..300] : clean;

        var who = (author ?? "").Trim();
        book.Author = who.Length switch
        {
            0 => null,
            > 200 => who[..200],
            _ => who,
        };

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteBookAsync(int id, CancellationToken ct = default)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == id, ct).ConfigureAwait(false);
        if (book is null) return;

        _db.Books.Remove(book);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        try { if (File.Exists(book.FilePath)) File.Delete(book.FilePath); } catch { /* best-effort */ }

        // Both caches are keyed by book id, so they have to go with the book — otherwise they sit
        // there forever, and a reused id would serve another book's pages.
        DeleteCached(_storage.PageCacheDir, id, "png");
        DeleteCached(_storage.TextCacheDir, id, "txt");
    }

    private static void DeleteCached(string dir, int bookId, string extension)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, $"{bookId}_*.{extension}"))
                try { File.Delete(file); } catch { /* best-effort */ }
        }
        catch { /* the directory may not exist yet */ }
    }
}
