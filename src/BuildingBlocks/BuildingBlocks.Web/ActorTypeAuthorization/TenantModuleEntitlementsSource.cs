using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.RateLimiting;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

/// <summary>
/// Implementación genérica de <see cref="ITenantModuleEntitlementsSource"/> para el gate de módulo
/// (Fase 1 log-only). Aplica la política de actor/tenant en un solo lugar y delega la lectura de
/// módulos a un <see cref="ITenantEntitlementModulesReader"/> que cada servicio implementa contra su
/// proyección local — así el rollout a más servicios agrega solo un lector EF + registro DI, sin
/// re-escribir esta lógica (DRY / Open-Closed).
///
/// <para>Fail-open deliberado en Fase 1: cualquier caso en que no se pueda determinar el módulo del
/// tenant devuelve <c>true</c> (no gatea), para no generar falsos "deny" mientras solo medimos.</para>
/// </summary>
public sealed class TenantModuleEntitlementsSource(ITenantEntitlementModulesReader reader) : ITenantModuleEntitlementsSource
{
    public async Task<bool> IsModuleEnabledAsync(ClaimsPrincipal user, string module, CancellationToken ct = default)
    {
        // PlatformAdmin y actores Service (M2M) no están sujetos al plan de un tenant.
        if (user.IsPlatformAdmin() || user.GetActorType() == ActorType.Service)
            return true;

        // El tenant SIEMPRE sale del token (aislamiento multitenant). Sin tenant resoluble no hay
        // nada que gatear.
        if (!user.TryGetTenantId(out var tenantId) || tenantId == Guid.Empty)
            return true;

        // null = proyección aún inexistente para el tenant (consistencia eventual) → no gatear.
        var enabledModules = await reader.GetEnabledModulesAsync(tenantId, ct);
        if (enabledModules is null)
            return true;

        return enabledModules.Contains(module, StringComparer.OrdinalIgnoreCase);
    }
}
