using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.Senders.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Senders;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

public sealed class SenderProfileRepository(CampaignsDbContext db) : ISenderProfileRepository
{
    public Task<SenderProfile?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db.SenderProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<SenderProfile>> GetManyByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default
    )
    {
        if (ids.Count == 0)
            return [];
        return await db
            .SenderProfiles.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && ids.Contains(s.Id))
            .ToListAsync(ct);
    }

    public async Task<PagedResult<SenderProfile>> ListAsync(
        Guid tenantId,
        CampaignChannel? channel,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db.SenderProfiles.IgnoreQueryFilters().Where(s => s.TenantId == tenantId);
        if (channel is { } c)
            query = query.Where(s => s.Channel == c);

        var ordered = query.OrderByDescending(s => s.CreatedAtUtc);
        var totalCount = await ordered.CountAsync(ct);
        var items = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<SenderProfile>(items, page, size, totalCount);
    }

    public async Task AddAsync(SenderProfile sender, CancellationToken ct = default) =>
        await db.SenderProfiles.AddAsync(sender, ct);
}
