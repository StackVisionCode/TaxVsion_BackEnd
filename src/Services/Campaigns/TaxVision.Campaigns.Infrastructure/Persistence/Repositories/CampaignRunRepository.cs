using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

public sealed class CampaignRunRepository(CampaignsDbContext db) : ICampaignRunRepository
{
    // IgnoreQueryFilters(): tenantId ya viene explícito y validado desde Application; el filtro
    // ambiental global no está garantizado poblado en el scope de DI de un handler de Wolverine.
    public Task<CampaignRun?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .CampaignRuns.IgnoreQueryFilters()
            .Include(r => r.Recipients)
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

    public Task<CampaignRun?> GetByDispatchIdAsync(Guid tenantId, string dispatchId, CancellationToken ct = default) =>
        db
            .CampaignRuns.IgnoreQueryFilters()
            .Include(r => r.Recipients)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Recipients.Any(x => x.DispatchId == dispatchId), ct);

    public Task<CampaignRun?> GetByRecipientIdAsync(Guid tenantId, Guid recipientId, CancellationToken ct = default) =>
        db
            .CampaignRuns.IgnoreQueryFilters()
            .Include(r => r.Recipients)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Recipients.Any(x => x.Id == recipientId), ct);

    public async Task<PagedResult<CampaignRun>> ListByCampaignAsync(
        Guid tenantId,
        Guid campaignId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db
            .CampaignRuns.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.CampaignId == campaignId)
            .OrderByDescending(r => r.CreatedAtUtc);

        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).Include(r => r.Recipients).ToListAsync(ct);
        return new PagedResult<CampaignRun>(items, page, size, totalCount);
    }

    public async Task AddAsync(CampaignRun run, CancellationToken ct = default) =>
        await db.CampaignRuns.AddAsync(run, ct);
}
