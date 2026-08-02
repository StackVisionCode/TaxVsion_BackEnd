using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Growth.Infrastructure.Persistence.RateLimiting;

/// <summary>
/// RateLimit Fase 2 — proyección local de "¿qué PlanCode tiene este tenant hoy?", mismo patrón
/// que Customer (Fase 6)/Tenant (Fase 2.1)/CloudStorage/Connectors, y que
/// <c>UserPermissionsProjection</c> (RBAC Fase 7/8) dentro de este mismo servicio. Implementa
/// <see cref="ITenantPlanCodeProjection"/> para que el handler compartido de BuildingBlocks pueda
/// operar sobre ella genéricamente.
///
/// <para>
/// Vive en Growth.Infrastructure (no en un proyecto "Growth.Domain", que no existe — Growth solo
/// tiene <c>Codes.Domain</c> y <c>Referrals.Domain</c>, ninguno de los dos bounded contexts al que
/// pertenezca esta proyección) — mismo lugar que <c>UserPermissionsProjection</c> y
/// <c>GrowthAuditEntry</c>, otros concerns transversales a ambos bounded contexts.
/// </para>
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
