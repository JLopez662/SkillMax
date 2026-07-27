using SkillMax.Application.Models;

namespace SkillMax.Application.Abstractions;

public interface IBookMetadataService
{
    /// <summary>
    /// A better title and author for a book than the one import could work out. Reads the PDF's
    /// own metadata where it has any worth trusting, and otherwise unpicks the filename the title
    /// came from — a leading scan date, a welded-on "by …".
    /// <para>
    /// Guesswork, deliberately surfaced rather than applied: <paramref name="currentTitle"/> is
    /// what the book is called now, and the caller shows what comes back for approval.
    /// </para>
    /// </summary>
    Task<BookDetails> SuggestAsync(string pdfPath, string currentTitle, CancellationToken ct = default);
}
