using BuildingBlocks.Caching;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.RateLimiting;

/// <summary>
/// A6/A5.6 — registro del lector de módulos del gate con su caché, en un solo sitio.
///
/// Los 13 servicios con el gate registraban su lector EF directo contra
/// <see cref="ITenantEntitlementModulesReader"/>, con el mismo bloque copiado en cada
/// <c>DependencyInjection.cs</c>. Meter la caché ahí habría multiplicado la copia por tres. Acá el
/// servicio dice solo cuál es SU lector y se lleva la caché ya puesta:
///
/// <code>
/// services.AddCachedTenantEntitlementModulesReader&lt;EfTenantEntitlementModulesReader&gt;();
/// </code>
///
/// El decorador queda además resoluble por su tipo concreto, que es lo que necesita el invalidador
/// de cada servicio para limpiarlo cuando llega el evento de entitlements.
/// </summary>
public static class TenantEntitlementModulesRegistration
{
    public static IServiceCollection AddCachedTenantEntitlementModulesReader<TReader>(this IServiceCollection services)
        where TReader : class, ITenantEntitlementModulesReader
    {
        services.AddScoped<TReader>();

        services.AddScoped(sp => new CachedTenantEntitlementModulesReader(
            sp.GetRequiredService<ICacheService>(),
            sp.GetRequiredService<TReader>()
        ));

        // La interfaz resuelve al MISMO scope que el tipo concreto (no una segunda instancia): el
        // invalidador y el gate tienen que hablar de la misma caché.
        services.AddScoped<ITenantEntitlementModulesReader>(sp =>
            sp.GetRequiredService<CachedTenantEntitlementModulesReader>()
        );

        return services;
    }
}
