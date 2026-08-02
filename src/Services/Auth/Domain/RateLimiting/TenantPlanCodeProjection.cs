using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Auth.Domain.RateLimiting;

/// <summary>
/// RateLimit Fase 2 — proyección local de "¿qué PlanCode tiene este tenant hoy?", mismo patrón
/// que Tenant/Customer y que <c>UserPermissionsProjection</c> (RBAC Fase 7) en los demás
/// servicios. Auth no tiene una entidad equivalente propia (es la fuente de verdad de User/Role,
/// no de PlanCode — ese dato pertenece a Subscription), así que esta proyección se mantiene igual
/// que en el resto de los servicios consumidores. Implementa <see cref="ITenantPlanCodeProjection"/>
/// para que el handler compartido de BuildingBlocks pueda operar sobre ella genéricamente.
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
