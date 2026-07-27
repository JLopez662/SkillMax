using System.Globalization;
using PDFtoImage;
using SkillMax.Application.Abstractions;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

public sealed class PdfImportService : IPdfImportService
{
    // Cover accents (mirror the mockup's book gradients).
    private static readonly string[] Palette =
    {
        "#4f5bd5", "#d62976", "#2b6a63", "#c0492f",
        "#8a5cc4", "#c78a24", "#2b8a8a", "#3f7b3a", "#d0417e",
    };

    private readonly AppDbContext _db;
    private readonly SkillMaxStorage _storage;

    public PdfImportService(AppDbContext db, SkillMaxStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<Book> ImportAsync(Stream pdf, string fileName, CancellationToken ct = default)
    {
        // Copy the picked PDF into app storage under a stable random name.
        var dest = Path.Combine(_storage.BooksDir, Guid.NewGuid().ToString("N") + ".pdf");
        await using (var fs = File.Create(dest))
            await pdf.CopyToAsync(fs, ct).ConfigureAwait(false);

        var bytes = await File.ReadAllBytesAsync(dest, ct).ConfigureAwait(false);
        int pageCount;
        try { pageCount = Conversion.GetPageCount(bytes); }
        catch { pageCount = 0; }

        var title = TitleFromFileName(fileName);
        var color = Palette[(uint)title.GetHashCode() % Palette.Length];

        var book = new Book
        {
            Title = title,
            FilePath = dest,
            PageCount = pageCount,
            ColorHex = color,
            ImportedUtc = DateTime.UtcNow,
        };

        _db.Books.Add(book);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return book;
    }

    private static string TitleFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName)
            .Replace('_', ' ').Replace('-', ' ').Trim();
        while (name.Contains("  ")) name = name.Replace("  ", " ");
        if (name.Length == 0) name = "Untitled";
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
    }
}
