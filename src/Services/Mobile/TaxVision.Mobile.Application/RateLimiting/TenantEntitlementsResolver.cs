using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using TaxVision.Mobile.Application.RateLimiting.Abstractions;
using TaxVision.Mobile.Domain.RateLimiting;

namespace TaxVision.Mobile.Application.RateLimiting;

/// <summary>
/// Reparación por pull del plan del tenant (patrón FinancialInsights): Subscription no repara a los consumidores,
/// así que un servicio nacido después de los datos o que perdió eventos se llena desde
/// <c>internal/tenants/{id}/entitlements</c>.
/// </summary>
public sealed class TenantEntitlementsResolver(
    ITenantPlanCodeProjectionRepository projections,
    ITenantEntitlementsSnapshotClient snapshots,
    ITenantPlanCodeCacheInvalidator cache,
    IUnitOfWork unitOfWork
)
{
    /// <summary>Trae el plan vigente y lo aplica si es igual o más nuevo. <c>false</c> si no hubo foto.</summary>
    public async Task<bool> RefreshAsync(Guid tenantId, CancellationToken ct = default)
    {
        var snapshot = await snapshots.FetchAsync(tenantId, ct);
        if (snapshot is null)
            return false;

        var row = await projections.GetAsync(tenantId, ct);
        if (row is null)
        {
            row = TenantPlanCodeProjection.Create(tenantId, snapshot.PlanCode, snapshot.RevisionNumber);
            row.ApplyIfNewer(snapshot.PlanCode, snapshot.RevisionNumber, snapshot.EnabledModules);
            // Otra escritura (el evento) pudo crearla a la vez: esa fila ya es tan nueva como esta foto.
            await projections.TryAddAndSaveAsync(row, ct);
        }
        else
        {
            row.ApplyIfNewer(snapshot.PlanCode, snapshot.RevisionNumber, snapshot.EnabledModules);
            await unitOfWork.SaveChangesAsync(ct);
        }

        await cache.InvalidateAsync(tenantId, ct);
        return true;
    }
}
