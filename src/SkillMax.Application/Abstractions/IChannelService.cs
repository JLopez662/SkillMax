using SkillMax.Application.Models;
using SkillMax.Domain.Entities;

namespace SkillMax.Application.Abstractions;

public interface IChannelService
{
    /// <summary>The channels the user follows, oldest first.</summary>
    Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Look up whatever the user typed — a handle, or any YouTube channel URL — and follow it.
    /// The channel is verified against YouTube before it's stored, so a typo is caught here
    /// rather than turning into a feed that quietly never shows anything.
    /// </summary>
    Task<ChannelAddResult> AddAsync(string input, CancellationToken ct = default);

    Task UnfollowAsync(int id, CancellationToken ct = default);
}
