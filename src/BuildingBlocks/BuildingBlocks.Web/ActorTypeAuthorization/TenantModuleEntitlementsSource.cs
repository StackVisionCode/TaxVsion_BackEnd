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
public sealed class TenantModuleEntitlementsSource(ITenantEntitlementModulesReader reader)
    : ITenantModuleEntitlementsSource
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

        // A6 — una lista VACÍA sí significa algo: "ningún módulo habilitado".
        //
        // Verificado en la base local antes de tocar nada: el tenant con `EnabledModulesJson='[]'`
        // tiene en Subscription un snapshot con los 4 módulos de su plan `starter` puestos a
        // "false" porque su suscripción está `Expired`. O sea que el `[]` es la traducción correcta
        // de "se le venció", no un fallo de la proyección.
        //
        // Se deja escrito porque es una trampa: fue mi primer instinto tratarlo como fail-open
        // —igual que la ausencia de fila— y eso le habría dado acceso COMPLETO a un tenant vencido
        // en cuanto el gate pase a enforce. Sin fila = no sé (fail-open). Lista vacía = sé que no
        // tiene nada (denegar).
        return enabledModules.Contains(module, StringComparer.OrdinalIgnoreCase);
    }
}
