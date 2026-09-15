using System.Text.Json;
using BuildingBlocks.Domain;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Customer.Domain.RateLimiting;

/// <summary>
/// Proyección local de "¿qué PlanCode tiene este tenant hoy?" — RateLimit Fase 6 (piloto
/// Customer), implementa el puerto <c>ITenantPlanCodeReader</c> de BuildingBlocks.RateLimiting
/// vía <c>EfTenantPlanCodeReader</c> (Infrastructure). Mismo patrón de proyección idempotente
/// por versión monotónica que <c>UserPermissionsProjection</c> — <c>RevisionNumber</c> viene de
/// <c>TenantEntitlementsChangedIntegrationEvent.RevisionNumber</c>, eventos fuera de orden se
/// ignoran. Implementa <see cref="ITenantPlanCodeProjection"/> (RateLimit Fase 1) para que el
/// handler de consumer compartido de BuildingBlocks pueda operar sobre ella genéricamente — la
/// tabla en sí sigue siendo propiedad exclusiva de Customer.
///
/// <para>
/// Gate de módulo Fase 1 (opt-in) — además del PlanCode persiste los módulos habilitados del plan
/// (<c>EnabledModulesJson</c>), alimentados por el mismo <c>TenantEntitlementsChanged</c>, para que
/// el <c>ITenantModuleEntitlementsSource</c> pueda loguear allow/deny por módulo (sin bloquear).
/// </para>
/// </summary>
public sealed class TenantPlanCodeProjection : TenantEntity, ITenantPlanCodeProjection
{
    private TenantPlanCodeProjection() { }

    public string PlanCode { get; private set; } = string.Empty;
    public long RevisionNumber { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Módulos comerciales habilitados por el plan (los <c>module.*</c> del snapshot), serializados como JSON.</summary>
    public string EnabledModulesJson { get; private set; } = "[]";

    /// <inheritdoc />
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
    /// persiste los módulos habilitados. Idempotente por revisión monotónica (eventos fuera de orden
    /// se ignoran, igual que la variante de 2 args).
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

    // Tolera "" (default de columna en filas preexistentes a la migración) y espacios → []; solo
    // parsea JSON real. Sin este guard, una fila con "" reventaría al leer EnabledModules.
    private static IReadOnlyList<string> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
