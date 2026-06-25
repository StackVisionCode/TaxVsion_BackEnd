using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tenant.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddTenantInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddDbContext<TenantDbContext>(opt =>
            opt.UseSqlServer(config.GetConnectionString("Default")));

        services.AddScoped<IUnitOfWork>(sp =>
            sp.GetRequiredService<TenantDbContext>());

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantReadService, TenantReadService>();

        return services;
    }
}
