using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace BuildingBlocks.Caching;

public static class CachingRegistration
{
    public static IServiceCollection AddRedisCache(
    this IServiceCollection services, IConfiguration config)
    {
        services.AddStackExchangeRedisCache(o =>
        {
            o.Configuration = config.GetConnectionString("Redis") ??
    "localhost:6379";
            o.InstanceName = "taxvision:";
        });
        services.AddScoped<ICacheService, RedisCacheService>();
        return services;
    }
}