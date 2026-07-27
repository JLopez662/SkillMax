using Microsoft.EntityFrameworkCore;
using SkillMax.Application;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

public sealed class AiPromptService : IAiPromptService
{
    /// <summary>Settings key holding the reader's edited template. No row means the default.</summary>
    private const string Key = "ai.prompt.template";

    private readonly AppDbContext _db;

    public AiPromptService(AppDbContext db) => _db = db;

    public async Task<string> GetTemplateAsync(CancellationToken ct = default)
    {
        var row = await _db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == Key, ct)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(row?.Value) ? AiPromptTemplate.Default : row!.Value;
    }

    public async Task SaveTemplateAsync(string? template, CancellationToken ct = default)
    {
        var value = template?.Trim();
        var row = await _db.Settings.FindAsync(new object[] { Key }, ct).ConfigureAwait(false);

        // Storing the default verbatim would freeze today's wording; clearing the row means a later
        // change to the default reaches readers who never edited theirs.
        if (string.IsNullOrWhiteSpace(value) || value == AiPromptTemplate.Default.Trim())
        {
            if (row is not null) _db.Settings.Remove(row);
        }
        else if (row is null)
        {
            _db.Settings.Add(new Setting { Key = Key, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<string> ComposeAsync(AiPromptContext context, CancellationToken ct = default) =>
        AiPromptTemplate.Render(await GetTemplateAsync(ct).ConfigureAwait(false), context);
}
