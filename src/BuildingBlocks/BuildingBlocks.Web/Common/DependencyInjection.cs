using BuildingBlocks.Common;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Web.Common;

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
