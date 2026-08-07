using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Application.RateLimiting.Abstractions;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Infrastructure.Idempotency;
using TaxVision.Postmaster.Infrastructure.Persistence;
using TaxVision.Postmaster.Infrastructure.Persistence.Repositories;
using TaxVision.Postmaster.Infrastructure.Providers;
using TaxVision.Postmaster.Infrastructure.Providers.Assets;
using TaxVision.Postmaster.Infrastructure.Providers.Connectors;
using TaxVision.Postmaster.Infrastructure.Providers.Smtp;
using TaxVision.Postmaster.Infrastructure.RateLimit;
using TaxVision.Postmaster.Infrastructure.RateLimiting;
using TaxVision.Postmaster.Infrastructure.Seed;

namespace TaxVision.Postmaster.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPostmasterInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<PostmasterDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<PostmasterDbContext>());
        services.AddSecretProtection();

        services.AddScoped<ISystemEmailProviderRepository, SystemEmailProviderRepository>();
        services.AddScoped<ITenantEmailProviderRepository, TenantEmailProviderRepository>();
        services.AddScoped<IProviderHealthStatusRepository, ProviderHealthStatusRepository>();
        services.AddScoped<ISentMessageRepository, SentMessageRepository>();
        services.AddScoped<ISuppressionListRepository, SuppressionListRepository>();
        services.AddScoped<ITenantOAuthAccountRepository, TenantOAuthAccountRepository>();
        services.AddScoped<IProviderResolver, ProviderResolver>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IOAuthProviderResolver, OAuthProviderResolver>();
        services.AddScoped<IIdempotencyGuard, SqlIdempotencyGuard>();
        services.Configure<SystemEmailProviderOptions>(
            configuration.GetSection(SystemEmailProviderOptions.SectionName)
        );
        services.AddHostedService<SystemEmailProviderSeeder>();

        services.AddSingleton(_ => new HttpResiliencePipelineRegistry(
            minimumThroughput: 5,
            onOpened: provider =>
                PostmasterMetrics.CircuitBreakerOpened.Add(1, new KeyValuePair<string, object?>("provider", provider))
        ));
        AddRateLimiting(services, configuration);

        AddCloudStorageAssetFetching(services, configuration);
        AddConnectorsSendClient(services, configuration);

        // RBAC Fase 7 (RBAC_Hardening_Plan.md) -- proyeccion local de permisos consultada por
        // ProjectionPermissionsSource cuando Authorization:PermissionsSource="Projection". La misma
        // instancia scoped satisface el puerto local rico (para los consumers) y el puerto
        // compartido y angosto de BuildingBlocks (para la autorizacion), evitando dos lecturas
        // separadas del mismo dato.
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

    // RateLimit Fase 2 — piezas siempre registradas: el consumer del evento de Subscription
    // (mantiene la proyección al día incluso con el flag apagado) y los lectores concretos. El
    // mapeo a ITenantPlanCodeReader/IPlanRateLimitReader (los que RateLimitQuotaResolver
    // realmente consume) es condicional al flag RateLimit:EnforceTierQuotas — decidido en
    // Program.cs, ANTES de AddTieredRateLimiting().
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
        // HttpPlanRateLimitReader (BuildingBlocks.Infrastructure.RateLimiting) depende del
        // contrato compartido; PostmasterServiceTokenAcquirer ya lo implementa (ver
        // AddCloudStorageAssetFetching), solo falta el forwarding.
        services.AddTransient<BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer>(sp =>
            (BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer)
                sp.GetRequiredService<IPostmasterServiceTokenAcquirer>()
        );

        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped<CachedTenantPlanCodeReader>(sp => new CachedTenantPlanCodeReader(
            sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
            sp.GetRequiredService<EfTenantPlanCodeReader>()
        ));
        services.AddScoped<
            BuildingBlocks.RateLimiting.ITenantPlanCodeCacheInvalidator,
            TenantPlanCodeCacheInvalidator
        >();

        services
            .AddOptions<SubscriptionClientOptions>()
            .Bind(configuration.GetSection(SubscriptionClientOptions.SectionName));
        services.AddHttpClient<HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    /// <summary>Si no hay Redis configurado (dev local) se degrada a un limiter no-op — mismo criterio que Signature Fase 4.</summary>
    private static void AddRateLimiting(IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddSingleton<IRateCounter, RedisRateCounter>();
            services.AddSingleton<IEmailProviderRateLimiter, RedisEmailProviderRateLimiter>();
        }
        else
        {
            services.AddSingleton<IEmailProviderRateLimiter, NoOpEmailProviderRateLimiter>();
        }
    }

    private static void AddCloudStorageAssetFetching(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services
            .AddOptions<CloudStorageClientOptions>()
            .Bind(configuration.GetSection(CloudStorageClientOptions.SectionName));

        // Fase 13 (hardening) — timeout 30s fijo en los 3 clientes M2M salientes de Postmaster, mismo
        // valor que Correspondence/Connectors ya usan en este mismo esfuerzo (ver ConnectorsClient/
        // PostmasterClient en Correspondence.Infrastructure.DependencyInjection) — consistencia entre
        // servicios y evita que una caída de Auth/CloudStorage cuelgue hasta el default de 100s.
        services.AddHttpClient<IPostmasterServiceTokenAcquirer, PostmasterServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        services.AddHttpClient<IInlineAssetFetcher, CloudStorageInlineAssetFetcher>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        services.AddHttpClient<IOutboundAttachmentFetcher, CloudStorageOutboundAttachmentFetcher>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static void AddConnectorsSendClient(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ConnectorsClientOptions>()
            .Bind(configuration.GetSection(ConnectorsClientOptions.SectionName));

        // Fase 13 (hardening) — mismo timeout 30s fijo que los clientes de CloudStorage/Auth arriba.
        services.AddHttpClient<IOAuthEmailSender, ConnectorsSendClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ConnectorsClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
