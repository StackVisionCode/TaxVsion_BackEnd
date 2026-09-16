using System.Security.Claims;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

/// <summary>
/// Fuente de "¿el tenant de este actor tiene habilitado el módulo <c>x</c> ahora mismo?" para el gate
/// de Entitlements en runtime. Cada servicio que quiera participar registra una implementación que lee
/// su proyección local de módulos (<c>TenantPlanCodeProjection.EnabledModules</c>, alimentada por
/// <c>TenantEntitlementsChangedIntegrationEvent</c>) — sin llamada HTTP en el hot path.
///
/// <para><b>Opt-in:</b> un servicio que NO registra esta fuente en DI no participa del gate de módulo
/// (el <see cref="PermissionPolicyProvider"/> resuelve <c>GetService</c> nullable y, si es null, no
/// evalúa nada). Así el rollout es gradual y sin falsos negativos en servicios aún no migrados.</para>
/// </summary>
public interface ITenantModuleEntitlementsSource
{
    /// <summary>
    /// <c>true</c> si el tenant del actor tiene el módulo habilitado. PlatformAdmin y actores Service
    /// (M2M) se consideran siempre habilitados (no están sujetos al plan de un tenant). Ante ausencia
    /// de proyección, la implementación decide su política (en Fase 1 log-only da igual: no bloquea).
    /// </summary>
    Task<bool> IsModuleEnabledAsync(ClaimsPrincipal user, string module, CancellationToken ct = default);
}
