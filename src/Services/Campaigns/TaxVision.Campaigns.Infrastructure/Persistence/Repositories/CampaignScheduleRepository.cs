using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Domain.Runs;
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

    public async Task<IReadOnlyList<ClaimedSchedule>> ClaimDueAsync(
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
            .Where(s =>
                claimableIds.Contains(s.Id)
                && (s.LeasedUntilUtc == null || s.LeasedUntilUtc < nowUtc)
                && s.ActiveRunId == null
            )
            .ExecuteUpdateAsync(
                set => set.SetProperty(s => s.LeaseToken, token).SetProperty(s => s.LeasedUntilUtc, until),
                ct
            );

        return await db
            .CampaignSchedules.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.LeaseToken == token && s.NextFireAtUtc != null)
            .Select(s => new ClaimedSchedule(s.Id, s.TenantId, token, s.NextFireAtUtc!.Value, until))
            .ToListAsync(ct);
    }

    public async Task AddAsync(CampaignSchedule schedule, CancellationToken ct = default) =>
        await db.CampaignSchedules.AddAsync(schedule, ct);

    public async Task<StuckScheduleRelease> ReleaseStuckAsync(
        DateTime nowUtc,
        TimeSpan grace,
        CancellationToken ct = default
    )
    {
        var closedBefore = nowUtc - grace;
        var stuck = db
            .CampaignSchedules.IgnoreQueryFilters()
            .Where(s =>
                s.Status == ScheduleStatus.Active
                && s.ActiveRunId != null
                && db.CampaignRuns.IgnoreQueryFilters()
                    .Any(r =>
                        r.Id == s.ActiveRunId
                        && r.Status != CampaignRunStatus.Created
                        && r.Status != CampaignRunStatus.Dispatching
                        && r.FinishedAtUtc != null
                        && r.FinishedAtUtc < closedBefore
                    )
            );

        // D-CMP-1: el horario ya pasó → se pausa (la oficina lo reanuda), no se dispara tarde.
        var paused = await stuck
            .Where(s => s.NextFireAtUtc != null && s.NextFireAtUtc <= nowUtc)
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(s => s.ActiveRunId, (Guid?)null)
                        .SetProperty(s => s.Status, ScheduleStatus.Paused)
                        .SetProperty(s => s.PauseReason, SchedulePauseReason.MissedSlot)
                        .SetProperty(s => s.UpdatedAtUtc, nowUtc),
                ct
            );
        var released = await stuck.ExecuteUpdateAsync(
            set => set.SetProperty(s => s.ActiveRunId, (Guid?)null).SetProperty(s => s.UpdatedAtUtc, nowUtc),
            ct
        );
        return new StuckScheduleRelease(released, paused);
    }

    // Consumer sin tenant ambiental: IgnoreQueryFilters + tenant explícito (viene del evento).
    public async Task<IReadOnlyList<CampaignSchedule>> ListOpenRunAsUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => await OpenRunAs(tenantId, userId).ToListAsync(ct);

    public Task<int> CountOpenRunAsUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        OpenRunAs(tenantId, userId).CountAsync(ct);

    public async Task<IReadOnlySet<Guid>> ListPausedOnOffboardingAsync(
        Guid tenantId,
        Guid fromUserId,
        CancellationToken ct = default
    ) =>
        (
            await db
                .CampaignScheduleHandovers.IgnoreQueryFilters()
                .Where(h =>
                    h.TenantId == tenantId
                    && h.FromUserId == fromUserId
                    && h.Outcome == CampaignScheduleHandoverOutcome.Paused
                )
                .Select(h => h.ScheduleId)
                .ToListAsync(ct)
        ).ToHashSet();

    public async Task AddHandoverAsync(CampaignScheduleHandover handover, CancellationToken ct = default) =>
        await db.CampaignScheduleHandovers.AddAsync(handover, ct);

    // EffectiveRunAsUserId no se traduce a SQL: se escribe la misma regla (heredado, o del creador sin heredero).
    private IQueryable<CampaignSchedule> OpenRunAs(Guid tenantId, Guid userId) =>
        db
            .CampaignSchedules.IgnoreQueryFilters()
            .Where(s =>
                s.TenantId == tenantId
                && (s.Status == ScheduleStatus.Active || s.Status == ScheduleStatus.Paused)
                && (s.RunAsUserId == userId || (s.RunAsUserId == null && s.CreatedByUserId == userId))
            );
}
