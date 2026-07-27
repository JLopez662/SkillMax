using Microsoft.EntityFrameworkCore;
using SkillMax.Application;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

public sealed class FeedService : IFeedService
{
    /// <summary>How many random pages are drawn per card before the weights pick between them.</summary>
    private const int CandidatesPerCard = 8;

    private readonly AppDbContext _db;

    public FeedService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<FeedCard>> GetFeedAsync(
        int count, int? categoryId = null, CancellationToken ct = default)
    {
        // Sequential reads only — the scoped DbContext is shared and not concurrency-safe.
        var books = await _db.Books
            .Where(b => !b.IsArchived && b.PageCount > 0)
            .Where(b => categoryId == null || b.CategoryId == categoryId)
            .ToListAsync(ct).ConfigureAwait(false);
        if (books.Count == 0) return Array.Empty<FeedCard>();

        var savedRows = await _db.SavedCards
            .Select(s => new { s.BookId, s.PageIndex })
            .ToListAsync(ct).ConfigureAwait(false);
        var saved = savedRows.Select(s => (s.BookId, s.PageIndex)).ToHashSet();

        var understoodRows = await _db.UnderstoodPages
            .Select(u => new { u.BookId, u.PageIndex })
            .ToListAsync(ct).ConfigureAwait(false);
        var understood = understoodRows.Select(u => (u.BookId, u.PageIndex)).ToHashSet();

        var rng = Random.Shared;

        // Weighting can't enumerate every page of every book — that's thousands of candidates for
        // a batch of six. Instead it over-draws uniformly and then lets the weights choose among
        // those, which costs a fixed amount and gives the same shape: pages already understood
        // rarely survive the second cut, and no page is ever ruled out.
        var candidates = new List<(Book Book, int Page)>(count * CandidatesPerCard);
        var seen = new HashSet<(int, int)>();
        for (var i = 0; i < count * CandidatesPerCard; i++)
        {
            var book = books[rng.Next(books.Count)];
            var page = rng.Next(book.PageCount);
            if (seen.Add((book.Id, page))) candidates.Add((book, page));
        }

        var drawn = FeedWeighting.Draw(
            candidates,
            candidate => FeedWeighting.WeightFor(understood.Contains((candidate.Book.Id, candidate.Page))),
            count,
            rng);

        var cards = new List<FeedCard>(drawn.Count);
        var touched = new HashSet<int>();

        // Carried down the batch so no two neighbouring cards draw the same background.
        string? previousBackground = null;

        foreach (var (book, page) in drawn)
        {
            touched.Add(book.Id);

            var background = CardBackgrounds.For(book.Id, page, previousBackground);
            previousBackground = background.Id;

            cards.Add(new FeedCard
            {
                BookId = book.Id,
                BookTitle = book.Title,
                Author = book.Author,
                PdfPath = book.FilePath,
                PageIndex = page,
                PageCount = book.PageCount,
                ColorHex = book.ColorHex,
                ProgressPct = Percent(book.PagesSeen, book.PageCount),
                Saved = saved.Contains((book.Id, page)),
                Understood = understood.Contains((book.Id, page)),
                Background = background,
            });
        }

        // Nudge progress forward for the books that appeared (approximate "pages seen").
        foreach (var book in books)
            if (touched.Contains(book.Id))
                book.PagesSeen = Math.Min(book.PageCount, book.PagesSeen + 1);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return cards;
    }

    private static int Percent(int seen, int total) =>
        total <= 0 ? 0 : (int)Math.Round(100.0 * Math.Min(seen, total) / total);
}
