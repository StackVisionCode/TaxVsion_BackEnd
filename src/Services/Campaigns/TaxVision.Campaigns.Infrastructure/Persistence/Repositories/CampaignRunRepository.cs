using System.Linq.Expressions;
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

    public Task<CampaignRun?> GetForResultAsync(
        Guid tenantId,
        Guid runId,
        string dispatchId,
        CancellationToken ct = default
    ) => LoadForResultAsync(tenantId, runId, x => x.DispatchId == dispatchId, ct);

    public async Task<CampaignRun?> GetForDispatchResultAsync(
        Guid tenantId,
        string dispatchId,
        CancellationToken ct = default
    )
    {
        var runId = await db
            .CampaignRecipients.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.DispatchId == dispatchId)
            .Select(r => (Guid?)r.RunId)
            .FirstOrDefaultAsync(ct);
        return runId is { } id ? await LoadForResultAsync(tenantId, id, x => x.DispatchId == dispatchId, ct) : null;
    }

    public async Task<CampaignRun?> GetForRecipientResultAsync(
        Guid tenantId,
        Guid recipientId,
        CancellationToken ct = default
    )
    {
        var runId = await db
            .CampaignRecipients.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.Id == recipientId)
            .Select(r => (Guid?)r.RunId)
            .FirstOrDefaultAsync(ct);
        return runId is { } id ? await LoadForResultAsync(tenantId, id, x => x.Id == recipientId, ct) : null;
    }

    // CMP-04: UPDLOCK pone en fila los resultados de un run hasta el commit, también entre réplicas. Sin él, en una
    // ráfaga casi todos pierden la RowVersion y los reintentos se agotan. La RowVersion sigue contra los demás escritores.
    private Task<CampaignRun?> LoadForResultAsync(
        Guid tenantId,
        Guid runId,
        Expression<Func<CampaignRecipient, bool>> unit,
        CancellationToken ct
    ) =>
        db
            .CampaignRuns.FromSqlInterpolated(
                $"SELECT * FROM CampaignRuns WITH (UPDLOCK, ROWLOCK) WHERE Id = {runId} AND TenantId = {tenantId}"
            )
            .IgnoreQueryFilters()
            .Include(r => r.Recipients.AsQueryable().Where(unit))
            .FirstOrDefaultAsync(ct);

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

    public async Task<IReadOnlyList<StaleRunRef>> ListStaleDispatchingAsync(
        DateTime createdBeforeUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .CampaignRuns.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.Status == CampaignRunStatus.Dispatching && r.CreatedAtUtc < createdBeforeUtc)
            .OrderBy(r => r.CreatedAtUtc)
            .Take(batchSize)
            .Select(r => new StaleRunRef(r.TenantId, r.Id))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StaleRunRef>> ListCreatedBeforeAsync(
        DateTime createdBeforeUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .CampaignRuns.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.Status == CampaignRunStatus.Created && r.CreatedAtUtc < createdBeforeUtc)
            .OrderBy(r => r.CreatedAtUtc)
            .Take(batchSize)
            .Select(r => new StaleRunRef(r.TenantId, r.Id))
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(Guid tenantId, Guid runId, CancellationToken ct = default) =>
        db.CampaignRuns.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenantId && r.Id == runId, ct);

    public Task<Guid?> GetScheduleIdAsync(Guid tenantId, Guid runId, CancellationToken ct = default) =>
        db
            .CampaignRuns.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.Id == runId)
            .Select(r => r.ScheduleId)
            .FirstOrDefaultAsync(ct);
}
