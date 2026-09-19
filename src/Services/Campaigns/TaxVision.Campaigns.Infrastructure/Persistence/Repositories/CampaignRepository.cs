using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

public sealed class CampaignRepository(CampaignsDbContext db) : ICampaignRepository
{
    // IgnoreQueryFilters(): tenantId ya viene explícito y validado desde Application; el filtro
    // ambiental global no está garantizado poblado en el scope de DI de un handler de Wolverine
    // (mismo patrón que NoteRepository.GetByIdAsync).
    public Task<Campaign?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .Campaigns.IgnoreQueryFilters()
            .Include(c => c.Senders)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);

    public async Task<PagedResult<Campaign>> ListAsync(
        Guid tenantId,
        CampaignStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db.Campaigns.IgnoreQueryFilters().Where(c => c.TenantId == tenantId);
        if (status is { } s)
            query = query.Where(c => c.Status == s);

        var ordered = query.OrderByDescending(c => c.CreatedAtUtc);

        var totalCount = await ordered.CountAsync(ct);
        var items = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<Campaign>(items, page, size, totalCount);
    }

    public async Task AddAsync(Campaign campaign, CancellationToken ct = default) =>
        await db.Campaigns.AddAsync(campaign, ct);
}
