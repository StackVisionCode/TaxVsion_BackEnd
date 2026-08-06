using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Notes.Domain.RateLimiting;

/// <summary>
/// RateLimit Fase 4 (de este plan) — proyección local de "¿qué PlanCode tiene este tenant hoy?",
/// mismo patrón que CloudStorage/Correspondence y que <c>UserPermissionsProjection</c> (RBAC Fase 3)
/// dentro de este mismo servicio. Implementa <see cref="ITenantPlanCodeProjection"/> para que el
/// handler compartido de BuildingBlocks pueda operar sobre ella genéricamente.
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
