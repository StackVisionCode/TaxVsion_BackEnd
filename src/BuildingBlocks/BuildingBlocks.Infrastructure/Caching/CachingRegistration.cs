using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Caching;

public static class CachingRegistration
{
    public static IServiceCollection AddRedisCache(this IServiceCollection services, IConfiguration config)
    {
        services.AddStackExchangeRedisCache(o =>
        {
            // Mismas opciones que AddTaxVisionRedis: Redis caído no bloquea y los comandos fallan al instante.
            o.ConfigurationOptions = RedisConnectionRegistration.CreateOptions(
                config.GetConnectionString("Redis") ?? "localhost:6379"
            );
            o.InstanceName = "taxvision:";
        });
        // BB-07 — Singleton, no Scoped: el coalescing de GetOrCreateAsync vive en la instancia, y
        // con una por request no hay nada que coalescer. IDistributedCache ya es Singleton.
        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
    }
}
