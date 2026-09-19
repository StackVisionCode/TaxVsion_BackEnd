using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Domain.Scheduling;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

public sealed class CampaignScheduleRepository(CampaignsDbContext db) : ICampaignScheduleRepository
{
    public Task<CampaignSchedule?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db.CampaignSchedules.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);

    public async Task<PagedResult<CampaignSchedule>> ListByCampaignAsync(
        Guid tenantId,
        Guid campaignId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db
            .CampaignSchedules.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.CampaignId == campaignId)
            .OrderByDescending(s => s.CreatedAtUtc);
        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<CampaignSchedule>(items, page, size, totalCount);
    }

    public Task<CampaignSchedule?> GetByActiveRunIdAsync(Guid tenantId, Guid runId, CancellationToken ct = default) =>
        db
            .CampaignSchedules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.ActiveRunId == runId, ct);

    public async Task<IReadOnlyList<CampaignSchedule>> ClaimDueAsync(
        DateTime nowUtc,
        int leaseSeconds,
        int batchSize,
        CancellationToken ct = default
    )
    {
        // Claim atómico con token único: el UPDATE marca los vencidos y claimables; luego se recuperan
        // exactamente los que este proceso ganó por su token. IgnoreQueryFilters porque el scheduler corre
        // sin contexto de tenant (el filtro global fail-closed no matchearía nada).
        var token = Guid.NewGuid();
        var until = nowUtc.AddSeconds(leaseSeconds);

        var claimableIds = await db
            .CampaignSchedules.IgnoreQueryFilters()
            .Where(s =>
                s.Status == ScheduleStatus.Active
                && s.NextFireAtUtc != null
                && s.NextFireAtUtc <= nowUtc
                && s.ActiveRunId == null
                && (s.LeasedUntilUtc == null || s.LeasedUntilUtc < nowUtc)
            )
            .OrderBy(s => s.NextFireAtUtc)
            .Take(batchSize)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (claimableIds.Count == 0)
            return [];

        await db
            .CampaignSchedules.IgnoreQueryFilters()
            .Where(s => claimableIds.Contains(s.Id) && (s.LeasedUntilUtc == null || s.LeasedUntilUtc < nowUtc) && s.ActiveRunId == null)
            .ExecuteUpdateAsync(
                set => set.SetProperty(s => s.LeaseToken, token).SetProperty(s => s.LeasedUntilUtc, until),
                ct
            );

        return await db.CampaignSchedules.IgnoreQueryFilters().Where(s => s.LeaseToken == token).ToListAsync(ct);
    }

    public async Task AddAsync(CampaignSchedule schedule, CancellationToken ct = default) =>
        await db.CampaignSchedules.AddAsync(schedule, ct);
}
