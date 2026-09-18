using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Campaigns.Application.Campaigns.Abstractions;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Infrastructure.Permissions;
using TaxVision.Campaigns.Infrastructure.Persistence;
using TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

namespace TaxVision.Campaigns.Infrastructure;

/// <summary>
/// Slice 1: DbContext + repositorio del aggregate Campaign. Las proyecciones RBAC/plan, el cliente
/// de Customer y los consumers de canal se agregan en slices posteriores (mismo patrón incremental
/// que Notes).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCampaignsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<CampaignsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CampaignsDbContext>());

        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<ICampaignRunRepository, CampaignRunRepository>();

        // RBAC (interino): reader fail-closed para ProjectionPermissionsSource. La proyección real
        // (poblada por consumers de Auth) es un slice posterior; hoy solo PlatformAdmin pasa [HasPermission].
        services.AddScoped<IUserPermissionsProjectionReader, EmptyUserPermissionsProjectionReader>();

        return services;
    }
}
