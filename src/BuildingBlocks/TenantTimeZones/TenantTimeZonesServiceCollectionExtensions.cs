using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.TenantTimeZones;

public static class TenantTimeZonesServiceCollectionExtensions
{
    /// <summary>
    /// Registra el store y el resolver sobre el DbContext del servicio. El servicio además mapea la entidad
    /// (<c>modelBuilder.ApplyTenantTimeZoneProjection(schema)</c>), crea su migración e incluye el consumer en Wolverine.
    /// </summary>
    public static IServiceCollection AddTenantTimeZoneProjection<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddMemoryCache();
        services.AddScoped<ITenantTimeZoneStore>(sp => new TenantTimeZoneStore(sp.GetRequiredService<TDbContext>()));
        services.AddScoped<ITenantTimeZoneResolver, TenantTimeZoneResolver>();
        return services;
    }

    /// <summary>
    /// Registra la reconciliación contra el directorio interno de Tenant (TZ13-D2): opciones, el token M2M del
    /// servicio, el HttpClient directo a Tenant.Api y el job.
    /// </summary>
    public static IServiceCollection AddTenantTimeZoneReconciliation<TTokenProvider>(
        this IServiceCollection services,
        IConfiguration configuration
    )
        where TTokenProvider : class, ITenantTimeZoneTokenProvider
    {
        services
            .AddOptions<TenantTimeZoneReconciliationOptions>()
            .Bind(configuration.GetSection(TenantTimeZoneReconciliationOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.TenantBaseUrl))
                    options.TenantBaseUrl = configuration[TenantTimeZoneReconciliationOptions.PlatformTenantBaseUrlKey] is { Length: > 0 } platform
                        ? platform
                        : TenantTimeZoneReconciliationOptions.LocalTenantBaseUrl;
            });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ITenantTimeZoneTokenProvider, TTokenProvider>();
        services.AddHttpClient<ITenantDirectoryZoneClient, TenantDirectoryZoneClient>(
            (sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<TenantTimeZoneReconciliationOptions>>().Value;
                var baseUrl = options.TenantBaseUrl.EndsWith('/') ? options.TenantBaseUrl : options.TenantBaseUrl + "/";
                http.BaseAddress = new Uri(baseUrl);
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
        services.AddHostedService<TenantTimeZoneReconciliationJob>();
        return services;
    }
}
