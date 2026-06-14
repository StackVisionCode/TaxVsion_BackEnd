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
    this IServiceCollection services, IConfiguration config)
    {
        // Registrar el DbContext con la cadena de conexión.
        services.AddDbContext<TenantDbContext>(opt =>
        opt.UseSqlServer(config.GetConnectionString("Default")));
        // El repositorio concreto detrás de la interfaz de Application.
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantReadService, TenantReadService>();
        return services;
    }
}
