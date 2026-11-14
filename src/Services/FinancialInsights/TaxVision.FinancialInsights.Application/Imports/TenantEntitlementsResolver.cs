using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Application.Projections.Abstractions;
using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Application.Imports;

/// <summary>
/// Plan del tenant desde la proyección local. Si falta la fila (servicio nuevo o evento perdido) la trae de
/// Subscription por M2M y la guarda: staff, portal y worker se reparan igual, y el API no acepta una importación
/// que el worker después dejaría esperando por falta de plan.
/// </summary>
public sealed class TenantEntitlementsResolver(
    IFinancialProjectionRepository projections,
    ITenantEntitlementsSnapshotClient snapshots,
    ITenantPlanCodeCacheInvalidator cache,
    IUnitOfWork unitOfWork,
    ILogger<TenantEntitlementsResolver> logger
)
{
    public async Task<TenantEntitlementsProjection?> GetOrPullAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await projections.FindTenantEntitlementsAsync(tenantId, ct);
        if (row is not null)
            return row;

        var snapshot = await snapshots.FetchAsync(tenantId, ct);
        if (snapshot is null)
            return null;

        row = Create(tenantId, snapshot);
        if (!await projections.TryAddTenantEntitlementsAsync(row, ct))
            return await projections.FindTenantEntitlementsAsync(tenantId, ct);

        await cache.InvalidateAsync(tenantId, ct);
        logger.LogInformation(
            "Entitlements projection repaired by pull for tenant {TenantId} (revision {Revision}).",
            tenantId,
            snapshot.RevisionNumber
        );
        return row;
    }

    /// <summary>Reconciliación: trae el plan vigente y lo aplica si es igual o más nuevo. <c>false</c> si no hubo foto.</summary>
    public async Task<bool> RefreshAsync(Guid tenantId, CancellationToken ct = default)
    {
        var snapshot = await snapshots.FetchAsync(tenantId, ct);
        if (snapshot is null)
            return false;

        var row = await projections.FindTenantEntitlementsAsync(tenantId, ct);
        if (row is null)
        {
            if (!await projections.TryAddTenantEntitlementsAsync(Create(tenantId, snapshot), ct))
                return false;
        }
        else
        {
            row.ApplyIfNewer(snapshot.PlanCode, snapshot.RevisionNumber, snapshot.EnabledModules, snapshot.DocumentsPerMonth);
            await unitOfWork.SaveChangesAsync(ct);
        }
        await cache.InvalidateAsync(tenantId, ct);
        return true;
    }

    private static TenantEntitlementsProjection Create(Guid tenantId, TenantEntitlementsSnapshot snapshot) =>
        TenantEntitlementsProjection.Create(
            tenantId,
            snapshot.PlanCode,
            snapshot.RevisionNumber,
            snapshot.EnabledModules,
            snapshot.DocumentsPerMonth
        );
}
