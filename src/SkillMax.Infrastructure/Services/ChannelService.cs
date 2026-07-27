using Microsoft.EntityFrameworkCore;
using SkillMax.Application;
using SkillMax.Application.Abstractions;
using SkillMax.Application.Models;
using SkillMax.Domain.Entities;
using SkillMax.Infrastructure.Persistence;

namespace SkillMax.Infrastructure.Services;

/// <summary>The followed-channel list behind the Library's Channels tab.</summary>
public sealed class ChannelService : IChannelService
{
    private readonly AppDbContext _db;
    private readonly YouTubeChannelCatalog _catalog;

    public ChannelService(AppDbContext db, YouTubeChannelCatalog catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken ct = default) =>
        await _db.Channels.OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false);

    public async Task<ChannelAddResult> AddAsync(string input, CancellationToken ct = default)
    {
        var query = ChannelInput.Parse(input);
        if (query is null) return new ChannelAddResult(ChannelAddOutcome.NotUnderstood);

        ResolvedChannel? resolved;
        try
        {
            resolved = await _catalog.ResolveAsync(query, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new ChannelAddResult(ChannelAddOutcome.Unreachable);
        }

        if (resolved is null) return new ChannelAddResult(ChannelAddOutcome.NotFound);

        // Matched on the UC… id, so pasting the handle for a channel added by URL (or the other
        // way round) is recognised as the same channel rather than following it twice.
        var existing = await _db.Channels
            .FirstOrDefaultAsync(c => c.ChannelId == resolved.ChannelId, ct)
            .ConfigureAwait(false);
        if (existing is not null) return new ChannelAddResult(ChannelAddOutcome.AlreadyFollowed, existing);

        var channel = new Channel
        {
            ChannelId = resolved.ChannelId,
            Handle = resolved.Handle,
            Title = resolved.Title,
            AvatarUrl = resolved.AvatarUrl,
            AddedUtc = DateTime.UtcNow,
        };

        _db.Channels.Add(channel);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new ChannelAddResult(ChannelAddOutcome.Added, channel);
    }

    public async Task UnfollowAsync(int id, CancellationToken ct = default)
    {
        var channel = await _db.Channels.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (channel is null) return;

        _db.Channels.Remove(channel);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Its cached listing is dead weight now, and stale if the channel is ever added back.
        _catalog.ForgetChannel(channel.ChannelId);
    }
}
