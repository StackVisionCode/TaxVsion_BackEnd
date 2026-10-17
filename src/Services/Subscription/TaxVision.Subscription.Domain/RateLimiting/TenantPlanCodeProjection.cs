using System.Text.Json;
using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Subscription.Domain.RateLimiting;

/// <summary>
/// RateLimit Fase 2 — proyección local de "¿qué PlanCode tiene este tenant hoy?", mismo patrón
/// que Tenant (Fase 2.1)/CloudStorage/Connectors y que <c>UserPermissionsProjection</c> (RBAC
/// Fase 7) dentro de este mismo servicio. Implementa <see cref="ITenantPlanCodeProjection"/> para
/// que el handler compartido de BuildingBlocks pueda operar sobre ella genéricamente.
/// </summary>
/// <remarks>
/// Subscription es quien PUBLICA <c>TenantEntitlementsChangedIntegrationEvent</c> (ver
/// RecalculateEntitlementsHandler) — esta proyección se llena consumiendo su propio evento vía el
/// mismo binding fanout que usan Auth/CloudStorage/etc. (la cola "subscription-events" ya está
/// bindeada al exchange completo "taxvision-events"), así que el consumer no necesita
/// infraestructura nueva. Se mantiene por consistencia con el resto de la flota — todos los
/// servicios leen su cuota vía el mismo <c>CachedTenantPlanCodeReader</c> con TTL de 5 min — en
/// vez de leer <c>TenantSubscription.PlanCode</c> directo por request, que rompería ese caché
/// compartido en el hot path de rate limiting.
/// </remarks>
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
