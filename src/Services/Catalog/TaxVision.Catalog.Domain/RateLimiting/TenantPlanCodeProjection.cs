using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Catalog.Domain.RateLimiting;

/// <summary>
/// RateLimit Fase 2 (plan de servicios nuevos) — proyección local de "¿qué PlanCode tiene este tenant
/// hoy?", mismo patrón que Notes/Customer y que <c>UserPermissionsProjection</c> (RBAC) dentro de este
/// mismo servicio. Implementa <see cref="ITenantPlanCodeProjection"/> para que el handler compartido
/// de BuildingBlocks pueda operar sobre ella genéricamente. La mantiene al día
/// <c>TenantPlanCodeProjectionConsumer</c> desde los eventos de Subscription.
/// </summary>
public sealed class TenantPlanCodeProjection : TenantEntity, ITenantPlanCodeProjection
{
    private TenantPlanCodeProjection() { }

    public string PlanCode { get; private set; } = string.Empty;
    public long RevisionNumber { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantPlanCodeProjection Create(Guid tenantId, string planCode, long revisionNumber)
    {
        var projection = new TenantPlanCodeProjection
        {
            Id = Guid.NewGuid(),
            PlanCode = planCode,
            RevisionNumber = revisionNumber,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void ApplyIfNewer(string planCode, long revisionNumber)
    {
        if (revisionNumber < RevisionNumber)
            return;
        PlanCode = planCode;
        RevisionNumber = revisionNumber;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
