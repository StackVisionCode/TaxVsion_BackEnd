using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Permissions.Abstractions;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Application.RateLimiting.Abstractions;
using TaxVision.Sms.Infrastructure.Persistence;
using TaxVision.Sms.Infrastructure.Persistence.Repositories;
using TaxVision.Sms.Infrastructure.Providers;
using TaxVision.Sms.Infrastructure.RateLimiting;

namespace TaxVision.Sms.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSmsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<SmsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<SmsDbContext>());

        services.AddScoped<ISmsMessageRepository, SmsMessageRepository>();
        services.AddScoped<ISmsOptOutRepository, SmsOptOutRepository>();
        services.AddScoped<IProcessedWebhookRepository, ProcessedWebhookRepository>();

        // RBAC Fase 7 — proyección local de permisos consultada por ProjectionPermissionsSource
        // cuando Authorization:PermissionsSource="Projection". La misma instancia scoped satisface
        // el puerto local rico (para los consumers) y el puerto compartido y angosto de
        // BuildingBlocks (para la autorización), evitando dos lecturas separadas del mismo dato.
        services.AddScoped<UserPermissionsProjectionRepository>();
        services.AddScoped<IUserPermissionsProjectionRepository>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IUserPermissionsProjectionReader>(sp =>
            sp.GetRequiredService<UserPermissionsProjectionRepository>()
        );
        services.AddScoped<IRolePermissionsProjectionRepository, RolePermissionsProjectionRepository>();

        // Config del servicio + de proveedores (sección `Sms`).
        services.AddOptions<SmsOptions>().Bind(configuration.GetSection(SmsOptions.SectionName));
        services.AddOptions<SmsProvidersOptions>().Bind(configuration.GetSection(SmsProvidersOptions.SectionName));

        // Adapters agnósticos (keyed DI por atributo) + factory + secretos de webhook.
        services.AddSmsProviders();

        // Router de plataforma: decide el orden de proveedores (primario + failover) desde la config
        // del servicio. No lo decide el tenant.
        services.AddScoped<ISmsProviderRouter, SmsProviderRouter>();

        // Reintentos + circuit-breaker para las llamadas salientes a proveedores (un HttpClient por adapter HTTP).
        services.AddSingleton(_ => new HttpResiliencePipelineRegistry());
        services.AddHttpClient(
            nameof(Providers.Generic.GenericHttpSmsProvider),
            http => http.Timeout = TimeSpan.FromSeconds(30)
        );
        services.AddHttpClient(
            nameof(Providers.Textmaxx.TextmaxxSmsProvider),
            http => http.Timeout = TimeSpan.FromSeconds(30)
        );
        services.AddHttpClient(
            nameof(Providers.Infobip.InfobipSmsProvider),
            http => http.Timeout = TimeSpan.FromSeconds(30)
        );
        services.AddHttpClient(
            nameof(Providers.Twilio.TwilioSmsProvider),
            http => http.Timeout = TimeSpan.FromSeconds(30)
        );

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
