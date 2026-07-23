using BuildingBlocks.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Web.Session;

public static class SessionDenylistServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="ISessionDenylistReader"/> (lectura Redis, fail-open) y
    /// <see cref="SessionDenylistOptions"/> para <see cref="SessionDenylistMiddleware"/>. Requiere
    /// que el servicio ya tenga <c>ICacheService</c> registrado (ver
    /// <c>BuildingBlocks.Caching.CachingRegistration.AddRedisCache</c>) — se registra ANTES de
    /// llamar a este método. No usar en Auth: Auth ya expone su propia implementación
    /// (<c>AccessTokenDenylist</c>, que también satisface esta interfaz porque necesita además
    /// escribir la denylist al revocar).
    /// </summary>
    public static IServiceCollection AddSessionDenylist(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SessionDenylistOptions>(configuration.GetSection(SessionDenylistOptions.SectionName));
        services.AddScoped<ISessionDenylistReader, SessionDenylistReader>();
        return services;
    }
}
