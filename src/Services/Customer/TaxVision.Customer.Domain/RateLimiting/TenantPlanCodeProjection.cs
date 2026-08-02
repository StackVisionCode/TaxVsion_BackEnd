using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.RateLimiting;

/// <summary>
/// Proyección local de "¿qué PlanCode tiene este tenant hoy?" — RateLimit Fase 6 (piloto
/// Customer), implementa el puerto <c>ITenantPlanCodeReader</c> de BuildingBlocks.RateLimiting
/// vía <c>EfTenantPlanCodeReader</c> (Infrastructure). Mismo patrón de proyección idempotente
/// por versión monotónica que <c>UserPermissionsProjection</c> — <c>RevisionNumber</c> viene de
/// <c>TenantEntitlementsChangedIntegrationEvent.RevisionNumber</c>, eventos fuera de orden se
/// ignoran.
/// </summary>
public sealed class TenantPlanCodeProjection : TenantEntity
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
