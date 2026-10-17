using System.Text.Json;
using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Notification.Domain.RateLimiting;

/// <summary>
/// RateLimit Fase 2 — proyección local de "¿qué PlanCode tiene este tenant hoy?", mismo patrón
/// que Customer (Fase 6) / Tenant (Fase 2.1) y que <c>AuthzUserPermissionsProjection</c> (RBAC
/// Fase 7) dentro de este mismo servicio. Implementa <see cref="ITenantPlanCodeProjection"/> para
/// que el handler compartido de BuildingBlocks pueda operar sobre ella genéricamente.
/// </summary>
public sealed class TenantPlanCodeProjection : TenantEntity, ITenantPlanCodeProjection
{
    private TenantPlanCodeProjection() { }

    public string PlanCode { get; private set; } = string.Empty;
    public long RevisionNumber { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public string EnabledModulesJson { get; private set; } = "[]";

    public IReadOnlyList<string> EnabledModules => Deserialize(EnabledModulesJson);

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

    public void ApplyIfNewer(string planCode, long revisionNumber) =>
        ApplyIfNewer(planCode, revisionNumber, EnabledModules);

    /// <summary>
    /// Override del default de <see cref="ITenantPlanCodeProjection"/>: además de plan+revisión,
    /// persiste los módulos habilitados. Idempotente por revisión monotónica.
    /// </summary>
    public void ApplyIfNewer(string planCode, long revisionNumber, IReadOnlyList<string> enabledModules)
    {
        if (revisionNumber < RevisionNumber)
            return;
        PlanCode = planCode;
        RevisionNumber = revisionNumber;
        EnabledModulesJson = Serialize(enabledModules);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string Serialize(IReadOnlyList<string> modules) => JsonSerializer.Serialize(modules);

    private static IReadOnlyList<string> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
