using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.TenantTimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Mobile.Application.Projections.Abstractions;
using TaxVision.Mobile.Application.RateLimiting;
using TaxVision.Mobile.Application.RateLimiting.Abstractions;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Application.Vehicles.Abstractions;
using TaxVision.Mobile.Infrastructure.Permissions;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Persistence.Repositories;
using TaxVision.Mobile.Infrastructure.RateLimiting;
using TaxVision.Mobile.Infrastructure.Retention;
using TaxVision.Mobile.Infrastructure.Upstream;

namespace TaxVision.Mobile.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMobileInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connection =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");
        services.AddDbContext<MobileDbContext>(options => options.UseSqlServer(connection));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MobileDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddHostedService<MileageGpsRetentionJob>();
        services.AddScoped<MobileProjectionRepository>();
        services.AddScoped<IMobileProjectionRepository>(sp => sp.GetRequiredService<MobileProjectionRepository>());
        services.AddScoped<IUserPermissionsProjectionReader>(sp => sp.GetRequiredService<MobileProjectionRepository>());
        services.AddScoped<IUserPermissionsProjectionWriter>(sp => sp.GetRequiredService<MobileProjectionRepository>());
        void ConfigureAuth(HttpClient http)
        {
            http.BaseAddress = new Uri(configuration["ServiceAuthClient:AuthBaseUrl"] ?? "http://localhost:5124/");
            http.Timeout = TimeSpan.FromSeconds(15);
        }
        services
            .AddHttpClient<IServiceTokenAcquirer, MobileServiceTokenAcquirer>(ConfigureAuth)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services
            .AddHttpClient<IPermissionsSnapshotClient, MobilePermissionsSnapshotClient>(ConfigureAuth)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddTenantTimeZoneProjection<MobileDbContext>();
        services.AddTenantTimeZoneReconciliation<MobileTenantTimeZoneTokenProvider>(configuration);
        AddTenantPlan(services, configuration);
        return services;
    }

    // Plan del tenant para el gate de mileage: proyección por evento + caché + reparación por pull.
    private static void AddTenantPlan(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddCachedTenantEntitlementModulesReader<EfTenantEntitlementModulesReader>();
        services.AddScoped(sp => new CachedTenantPlanCodeReader(
            sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
            sp.GetRequiredService<EfTenantPlanCodeReader>()
        ));
        services.AddScoped<ITenantPlanCodeCacheInvalidator, TenantPlanCodeCacheInvalidator>();

        services.AddScoped<TenantEntitlementsResolver>();
        services.AddScoped<TenantEntitlementsRefreshTargets>();
        services
            .AddHttpClient<ITenantEntitlementsSnapshotClient, SubscriptionEntitlementsSnapshotClient>(http =>
            {
                http.BaseAddress = new Uri(configuration["SubscriptionClient:BaseUrl"] ?? "http://localhost:5360/");
                http.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services
            .AddOptions<TenantEntitlementsReconciliationOptions>()
            .Bind(configuration.GetSection(TenantEntitlementsReconciliationOptions.SectionName));
        services.AddHostedService<TenantEntitlementsReconciliationJob>();
    }
}
