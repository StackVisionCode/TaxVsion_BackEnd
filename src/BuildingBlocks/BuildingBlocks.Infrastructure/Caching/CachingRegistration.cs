using BuildingBlocks.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Caching;

public static class CachingRegistration
{
    public static IServiceCollection AddRedisCache(this IServiceCollection services, IConfiguration config)
    {
        services.AddStackExchangeRedisCache(o =>
        {
            o.Configuration = config.GetConnectionString("Redis") ?? "localhost:6379";
            o.InstanceName = "taxvision:";
        });
        // BB-07 — Singleton, no Scoped: el coalescing de GetOrCreateAsync vive en la instancia, y
        // con una por request no hay nada que coalescer. IDistributedCache ya es Singleton.
        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
    }
}
