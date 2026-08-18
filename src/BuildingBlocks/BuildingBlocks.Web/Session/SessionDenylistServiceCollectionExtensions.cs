using BuildingBlocks.Infrastructure.Sessions;
using BuildingBlocks.Sessions;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Web.Session;

public static class SessionDenylistServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="ISessionDenylistReader"/> (lectura Redis, fail-open) y
    /// <see cref="SessionDenylistOptions"/> para <see cref="SessionDenylistMiddleware"/>. Requiere
    /// que el servicio ya tenga <c>ICacheService</c> registrado (ver
    /// <c>BuildingBlocks.Infrastructure.Caching.CachingRegistration.AddRedisCache</c>) — se registra ANTES de
    /// llamar a este método. No usar en Auth: Auth ya expone su propia implementación
    /// (<c>AccessTokenDenylist</c>, que también satisface esta interfaz porque necesita además
    /// escribir la denylist al revocar).
    /// </summary>
    public static IServiceCollection AddSessionDenylist(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SessionDenylistOptions>(configuration.GetSection(SessionDenylistOptions.SectionName));
        services.AddScoped<ISessionDenylistReader, SessionDenylistReader>();

        // H-06 — el middleware cuenta los fallos del store en authz.session_denylist_unavailable.
        // AddActorTypeAuthorization ya lo registra en los 17, pero dejar el helper autocontenido evita
        // que un servicio futuro se lo pierda (misma lección que BB-01).
        services.TryAddSingleton<AuthorizationMetrics>();
        return services;
    }
}
