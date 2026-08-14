using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Tasks.Domain.RateLimiting;

/// <summary>
/// Qué PlanCode tiene este tenant hoy, alimentado por <c>TenantEntitlementsChangedIntegrationEvent</c>
/// de Subscription. Es la mitad local del escalado de cuotas por tier; el multiplicador que ese plan
/// aplica a cada categoría vive en Subscription y se lee por HTTP M2M.
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
