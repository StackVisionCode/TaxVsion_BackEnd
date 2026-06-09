using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks;

public static class BuildingBlocksRegistration
{



    public static IServiceCollection AddBuildingBlocks(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddScoped<CorrelationContext>();
        services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());


        return services;
    }
}
