using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Catalog.Application.Abstractions;
using TaxVision.Catalog.Application.Permissions.Abstractions;
using TaxVision.Catalog.Infrastructure.Persistence;
using TaxVision.Catalog.Infrastructure.Persistence.Repositories;

namespace TaxVision.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<CatalogDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CatalogDbContext>());

        services.AddScoped<ICatalogItemRepository, CatalogItemRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // RBAC Fase 7 — proyección local de permisos consultada por ProjectionPermissionsSource. La
        // misma instancia scoped satisface el puerto local rico y el puerto compartido de BuildingBlocks.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        return services;
    }
}
