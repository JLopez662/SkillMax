using Microsoft.EntityFrameworkCore;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

public sealed class SavedCardService : ISavedCardService
{
    private readonly AppDbContext _db;

    public SavedCardService(AppDbContext db) => _db = db;

    public async Task<bool> ToggleAsync(int bookId, int pageIndex, CancellationToken ct = default)
    {
        var existing = await _db.SavedCards
            .FirstOrDefaultAsync(s => s.BookId == bookId && s.PageIndex == pageIndex, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _db.SavedCards.Remove(existing);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return false;
        }

        _db.SavedCards.Add(new SavedCard { BookId = bookId, PageIndex = pageIndex, SavedUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public Task<bool> IsSavedAsync(int bookId, int pageIndex, CancellationToken ct = default) =>
        _db.SavedCards.AnyAsync(s => s.BookId == bookId && s.PageIndex == pageIndex, ct);

    public async Task<IReadOnlyList<FeedCard>> GetSavedAsync(CancellationToken ct = default)
    {
        var rows = await _db.SavedCards
            .OrderByDescending(s => s.SavedUtc)
            .Join(_db.Books, s => s.BookId, b => b.Id, (s, b) => new { s, b })
            .ToListAsync(ct).ConfigureAwait(false);

        var cards = new List<FeedCard>(rows.Count);
        string? previousBackground = null;

        foreach (var r in rows)
        {
            var background = CardBackgrounds.For(r.b.Id, r.s.PageIndex, previousBackground);
            previousBackground = background.Id;

            cards.Add(new FeedCard
            {
                BookId = r.b.Id,
                BookTitle = r.b.Title,
                Author = r.b.Author,
                PdfPath = r.b.FilePath,
                PageIndex = r.s.PageIndex,
                PageCount = r.b.PageCount,
                ColorHex = r.b.ColorHex,
                ProgressPct = r.b.PageCount <= 0 ? 0 : (int)Math.Round(100.0 * Math.Min(r.b.PagesSeen, r.b.PageCount) / r.b.PageCount),
                Saved = true,
                Background = background,
            });
        }

        return cards;
    }
}
