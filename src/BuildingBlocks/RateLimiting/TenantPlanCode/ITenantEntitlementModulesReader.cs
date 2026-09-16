namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Gate de módulo Fase 1 — lee los módulos habilitados del plan de un tenant desde la proyección
/// local (<c>TenantPlanCodeProjection.EnabledModules</c>, alimentada por
/// <c>TenantEntitlementsChangedIntegrationEvent</c>). Cada servicio que hace opt-in del gate
/// implementa este puerto en su Infrastructure (lectura EF <c>AsNoTracking</c>); la fuente genérica
/// <c>TenantModuleEntitlementsSource</c> de BuildingBlocks.Web lo consume y aplica la política de
/// actor/tenant en un solo lugar.
///
/// <para>Vive junto a <see cref="ITenantPlanCodeReader"/> porque lee la misma proyección; separado
/// de él porque el rate-limit solo necesita el <c>PlanCode</c> (string) y el gate necesita la lista
/// de módulos — dos preocupaciones distintas sobre la misma fila (Interface Segregation).</para>
/// </summary>
public interface ITenantEntitlementModulesReader
{
    /// <summary>
    /// Módulos habilitados del tenant, o <c>null</c> si aún no existe proyección para él (evento no
    /// consumido todavía). <c>null</c> ≠ "sin módulos": el llamador NO debe gatear un tenant sin
    /// proyección (evita falsos "deny" durante la consistencia eventual).
    /// </summary>
    Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default);
}
