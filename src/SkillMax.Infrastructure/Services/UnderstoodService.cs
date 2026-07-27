using Microsoft.EntityFrameworkCore;
using SkillMax.Application.Abstractions;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

public sealed class UnderstoodService : IUnderstoodService
{
    private readonly AppDbContext _db;

    public UnderstoodService(AppDbContext db) => _db = db;

    public async Task<bool> TogglePageAsync(int bookId, int pageIndex, CancellationToken ct = default)
    {
        var existing = await _db.UnderstoodPages
            .FirstOrDefaultAsync(u => u.BookId == bookId && u.PageIndex == pageIndex, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _db.UnderstoodPages.Remove(existing);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return false;
        }

        _db.UnderstoodPages.Add(new UnderstoodPage
        {
            BookId = bookId,
            PageIndex = pageIndex,
            MarkedUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> ToggleShortAsync(string videoId, CancellationToken ct = default)
    {
        var existing = await _db.UnderstoodShorts
            .FirstOrDefaultAsync(u => u.VideoId == videoId, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _db.UnderstoodShorts.Remove(existing);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return false;
        }

        _db.UnderstoodShorts.Add(new UnderstoodShort { VideoId = videoId, MarkedUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<int>> GetPagesAsync(int bookId, CancellationToken ct = default) =>
        await _db.UnderstoodPages
                 .Where(u => u.BookId == bookId)
                 .Select(u => u.PageIndex)
                 .ToListAsync(ct)
                 .ConfigureAwait(false);

    public async Task<int> CountAsync(CancellationToken ct = default) =>
        await _db.UnderstoodPages.CountAsync(ct).ConfigureAwait(false) +
        await _db.UnderstoodShorts.CountAsync(ct).ConfigureAwait(false);
}
