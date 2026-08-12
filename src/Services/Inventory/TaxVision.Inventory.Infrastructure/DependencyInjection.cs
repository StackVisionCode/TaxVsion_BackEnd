using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Application.Permissions.Abstractions;
using TaxVision.Inventory.Infrastructure.Persistence;
using TaxVision.Inventory.Infrastructure.Persistence.Repositories;

namespace TaxVision.Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<InventoryDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<InventoryDbContext>());

        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IItemSupplierRepository, ItemSupplierRepository>();

        // RBAC Fase 7 — proyección local de permisos.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(sp => sp.GetRequiredService<UserPermissionsProjectionRepository>());
        services.AddScoped<IUserPermissionsProjectionReader>(sp => sp.GetRequiredService<UserPermissionsProjectionRepository>());
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        return services;
    }
}
