using System.Text.RegularExpressions;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using UglyToad.PdfPig;

namespace SkillMax.Infrastructure.Services;

/// <summary>
/// Works out what a book should be called. Prefers the PDF's own Title and Author, and falls back
/// to unpicking the filename — which is all import ever had to go on, so it's what every book's
/// title starts as.
/// <para>
/// Everything here is a guess, which is why nothing is saved from it: the rename sheet fills its
/// fields and the reader approves. That's what makes the "by …" split safe to attempt at all —
/// it would otherwise maul a title like "Gone by Midnight".
/// </para>
/// DB-free and file-bound, so it's a singleton alongside the other PDF services.
/// </summary>
public sealed class BookMetadataService : IBookMetadataService
{
    /// <summary>"2026 02 27 The 12 Week Year" — a scan date stamped on the front, not the title.</summary>
    private static readonly Regex LeadingDate = new(
        @"^\s*(19|20)\d{2}[\s._-]+\d{1,2}[\s._-]+\d{1,2}[\s._-]+",
        RegexOptions.Compiled);

    /// <summary>"… By Brian P. Moran" — an author welded onto the end of the filename.</summary>
    private static readonly Regex TrailingBy = new(
        @"\s+by\s+(?<author>[^\d]{2,60})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Producers that write the source document's filename into the Title field.</summary>
    private static readonly Regex JunkTitle = new(
        @"^(microsoft word|microsoft powerpoint|untitled|document\s*\d*$)|\.(docx?|pdf|indd|pages|rtf|tex)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Runs = new(@"\s{2,}", RegexOptions.Compiled);

    public async Task<BookDetails> SuggestAsync(
        string pdfPath, string currentTitle, CancellationToken ct = default)
    {
        var info = await Task.Run(() => ReadInfo(pdfPath), ct).ConfigureAwait(false);

        // The filename is unpicked either way: a PDF that names a title but no author is the
        // common case, and the author is usually sitting right there in the filename.
        var (fileTitle, fileAuthor) = FromTitle(currentTitle);

        var title = Usable(info.Title) ? Tidy(info.Title!) : fileTitle;
        var author = Usable(info.Author) ? Tidy(info.Author!) : fileAuthor;

        return new BookDetails(Cap(title, 300) ?? "Untitled", Cap(author, 200));
    }

    private static (string? Title, string? Author) ReadInfo(string pdfPath)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            return (doc.Information.Title, doc.Information.Author);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Present, long enough to be a name, and not a filename the producer left behind.</summary>
    private static bool Usable(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 2 && !JunkTitle.IsMatch(value.Trim());

    private static (string Title, string? Author) FromTitle(string? raw)
    {
        var name = Tidy(LeadingDate.Replace(raw ?? "", ""));
        string? author = null;

        var match = TrailingBy.Match(name);
        if (match.Success)
        {
            author = Tidy(match.Groups["author"].Value);
            name = Tidy(name[..match.Index]);
        }

        // A title that was nothing but a date and an author is worse than the one we started with.
        if (name.Length == 0) name = Tidy(raw ?? "");
        if (name.Length == 0) name = "Untitled";

        return (name, author);
    }

    private static string Tidy(string value) => Runs.Replace(value.Trim(), " ");

    private static string? Cap(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
