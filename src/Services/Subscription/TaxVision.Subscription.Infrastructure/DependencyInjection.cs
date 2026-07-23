using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Infrastructure.Scheduling;

namespace TaxVision.Subscription.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSubscriptionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<SubscriptionDbContext>(options => options.UseSqlServer(connectionString));
        services.Configure<SubscriptionOptions>(configuration.GetSection(SubscriptionOptions.SectionName));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<SubscriptionDbContext>());
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ISubscriptionRepository, TenantSubscriptionRepository>();
        services.AddScoped<ISubscriptionSeatRepository, SubscriptionSeatRepository>();
        services.AddScoped<ISubscriptionTenantSettingsRepository, SubscriptionTenantSettingsRepository>();
        services.AddScoped<IAddOnDefinitionRepository, AddOnDefinitionRepository>();
        services.AddScoped<ITenantAddOnRepository, TenantAddOnRepository>();
        services.AddScoped<ITenantEntitlementSnapshotRepository, TenantEntitlementSnapshotRepository>();
        services.AddScoped<ISubscriptionAuditLogWriter, SubscriptionAuditLogWriter>();
        services.AddScoped<ISubscriptionAuditLogRepository, SubscriptionAuditLogRepository>();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "localhost:6379")
        );
        services.AddSingleton<IDistributedLockFactory, RedisDistributedLockFactory>();

        // RBAC Fase 7 (RBAC_Hardening_Plan.md) -- Subscription solo recibe la proyeccion de
        // sincronizacion (sin wiring de enforcement: no usa [HasPermission]/PermissionPolicyProvider
        // todavia, eso es Fase 8). Se construye ahora para que ya este al dia cuando esa fase active
        // el mecanismo.
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
