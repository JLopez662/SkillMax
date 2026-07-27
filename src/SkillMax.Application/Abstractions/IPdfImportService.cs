using SkillMax.Domain.Entities;

namespace SkillMax.Application.Abstractions;

public interface IPdfImportService
{
    /// <summary>Copy a picked PDF into app storage, count its pages, and create the Book.</summary>
    Task<Book> ImportAsync(Stream pdf, string fileName, CancellationToken ct = default);
}
