using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Catalog.Application.Abstractions;
using TaxVision.Catalog.Application.Permissions.Abstractions;
using TaxVision.Catalog.Application.RateLimiting.Abstractions;
using TaxVision.Catalog.Infrastructure.Persistence;
using TaxVision.Catalog.Infrastructure.Persistence.Repositories;
using TaxVision.Catalog.Infrastructure.RateLimiting;

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

        AddRateLimitTierQuotas(services, configuration);

        return services;
    }

    // RateLimit Fase 2 — piezas siempre registradas: los lectores concretos de la proyección local
    // (Ef + caché) y el acquirer M2M + HttpPlanRateLimitReader para leer el catálogo de PlanRateLimits
    // de Subscription. El mapeo a BuildingBlocks.RateLimiting.ITenantPlanCodeReader/IPlanRateLimitReader
    // que RateLimitQuotaResolver consume vive en Program.cs, condicional al flag RateLimit:EnforceTierQuotas
    // (OFF por default → fail-open a la cuota base). El consumer que mantiene la proyección corre siempre.
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped<BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader>(
            sp => new BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader(
                sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
                sp.GetRequiredService<EfTenantPlanCodeReader>()
            )
        );
        services.AddScoped<
            BuildingBlocks.RateLimiting.ITenantPlanCodeCacheInvalidator,
            TenantPlanCodeCacheInvalidator
        >();

        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
            }
        );

        services
            .AddOptions<BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions>()
            .Bind(
                configuration.GetSection(
                    BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions.SectionName
                )
            );
        services.AddHttpClient<BuildingBlocks.Infrastructure.RateLimiting.HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<
                    IOptions<BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions>
                >().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
