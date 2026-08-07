using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.RateLimiting.Abstractions;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Jobs;
using TaxVision.Connectors.Infrastructure.Locking;
using TaxVision.Connectors.Infrastructure.OAuth;
using TaxVision.Connectors.Infrastructure.Observability;
using TaxVision.Connectors.Infrastructure.Persistence;
using TaxVision.Connectors.Infrastructure.Persistence.Repositories;
using TaxVision.Connectors.Infrastructure.Providers;
using TaxVision.Connectors.Infrastructure.Providers.Gmail;
using TaxVision.Connectors.Infrastructure.Providers.Graph;
using TaxVision.Connectors.Infrastructure.Providers.Imap;
using TaxVision.Connectors.Infrastructure.Providers.Manual;
using TaxVision.Connectors.Infrastructure.Providers.OAuth;
using TaxVision.Connectors.Infrastructure.Providers.Watch;
using TaxVision.Connectors.Infrastructure.RateLimit;
using TaxVision.Connectors.Infrastructure.RateLimiting;
using TaxVision.Connectors.Infrastructure.Security;
using TaxVision.Connectors.Infrastructure.Watch;

namespace TaxVision.Connectors.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddConnectorsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<ConnectorsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ConnectorsDbContext>());

        services.AddRotatingSecretProtection(configuration);
        services.AddSingleton<IEncryptedSecretProtector, EncryptedSecretProtector>();

        services.AddScoped<ITenantEmailAccountRepository, TenantEmailAccountRepository>();
        services.AddScoped<IOAuthConnectionRepository, OAuthConnectionRepository>();
        services.AddScoped<IImapCredentialsRepository, ImapCredentialsRepository>();
        services.AddScoped<ISmtpCredentialsRepository, SmtpCredentialsRepository>();
        services.AddScoped<IManualAccountConnectivityValidator, ManualAccountConnectivityValidator>();

        // Distributed lock + rate limiter (Redis). Sin connection string, degrada a no-op/single-node.
        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddSingleton<IDistributedLock, RedisDistributedLock>();
            services.AddSingleton<IRateCounter, RedisRateCounter>();
            services.AddSingleton<IProviderRateLimiter, RedisProviderRateLimiter>();
        }
        else
        {
            services.AddSingleton<IDistributedLock, NoOpDistributedLock>();
            services.AddSingleton<IProviderRateLimiter, InMemoryProviderRateLimiter>();
        }

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
            services.AddSingleton<IOAuthConnectStateStore, RedisOAuthConnectStateStore>();
        else
            services.AddSingleton<IOAuthConnectStateStore, InMemoryOAuthConnectStateStore>();

        services.Configure<ProviderRateLimiterOptions>(configuration.GetSection("Connectors:RateLimit"));
        services.AddSingleton(sp => new HttpResiliencePipelineRegistry(
            onRetry: key => ConnectorsMetrics.RetryAttempts.Add(1, new KeyValuePair<string, object?>("provider", key)),
            onOpened: key =>
            {
                sp.GetRequiredService<ILogger<HttpResiliencePipelineRegistry>>()
                    .LogWarning("Circuit breaker opened for {Key}.", key);
                ConnectorsMetrics.CircuitBreakerOpened.Add(1, new KeyValuePair<string, object?>("provider", key));
            }
        ));

        services.Configure<GoogleOAuthOptions>(configuration.GetSection(GoogleOAuthOptions.SectionName));
        services.Configure<MicrosoftOAuthOptions>(configuration.GetSection(MicrosoftOAuthOptions.SectionName));
        // Fase 3 (hardening) — timeout 30s fijo en todos los HttpClient tipados de Connectors, mismo
        // valor que Correspondence/Postmaster ya usan en este mismo esfuerzo (ver ConnectorsClient/
        // PostmasterClient en Correspondence.Infrastructure.DependencyInjection y los 3 clientes M2M
        // de Postmaster.Infrastructure.DependencyInjection). Antes solo 3 de ~7 rutas externas se
        // protegían con un CancellationTokenSource ad-hoc por-llamada (GetMessageBodyHandler/
        // GetMessageAttachmentHandler/SendMessageHandler); moverlo acá lo aplica uniformemente a
        // TODAS las rutas, incluidas las que no tenían ningún cap: RawMessageSyncOrchestrator
        // (llamadas en loop disparadas por webhook) y los background jobs (ProactiveTokenRefreshJob,
        // WatchRenewalJob) — sin esto, un proveedor lento (no caído — caído ya lo cubre el circuit
        // breaker) podía retener la request síncrona del webhook o una iteración del job hasta 100s
        // por llamada, sin cap, acumulándose bajo carga.
        services
            .AddHttpClient<GoogleOAuthClient>()
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<MicrosoftOAuthClient>().ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IOAuthProviderClientFactory, OAuthProviderClientFactory>();
        // Admin-consent fallback (D3 §12.6) — solo Graph, mismo cliente HTTP tipado ya registrado arriba.
        services.AddScoped<IMicrosoftAdminConsentClient>(sp => sp.GetRequiredService<MicrosoftOAuthClient>());

        services.AddScoped<IOAuthTokenManager, OAuthTokenManager>();
        services.AddHostedService<ProactiveTokenRefreshJob>();

        services.AddHttpClient<GmailApiClient>().ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<GraphApiClient>().ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<ImapClient>();
        services.AddScoped<IEmailProviderClientFactory, EmailProviderClientFactory>();
        services.AddScoped<SmtpManualClient>();
        services.AddScoped<IOutboundEmailProviderClientFactory, OutboundEmailProviderClientFactory>();

        // Envío (D3 §3.5) — per-cuenta, distinto de IProviderRateLimiter (global por proveedor).
        services.Configure<SendRateLimiterOptions>(configuration.GetSection("Connectors:SendRateLimit"));
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
            services.AddSingleton<ISendRateLimiter, RedisSendRateLimiter>();
        else
            services.AddSingleton<ISendRateLimiter, InMemorySendRateLimiter>();

        services.AddScoped<IProviderWatchSubscriptionRepository, ProviderWatchSubscriptionRepository>();
        services.Configure<GmailWatchOptions>(configuration.GetSection(GmailWatchOptions.SectionName));
        services.Configure<GraphWatchOptions>(configuration.GetSection(GraphWatchOptions.SectionName));
        services.AddHttpClient<GmailWatchClient>().ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<GraphWatchClient>().ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<IWatchProviderClientFactory, WatchProviderClientFactory>();
        services.AddScoped<IWatchRenewalService, WatchRenewalService>();
        services.AddHostedService<WatchRenewalJob>();

        services.AddScoped<IProviderSyncCursorRepository, ProviderSyncCursorRepository>();

        // Reconciliación (safety net Gmail/Graph detrás del push, único mecanismo de sync IMAP) —
        // README §37.8. Reusa ITenantEmailAccountRepository/IProviderWatchSubscriptionRepository/
        // IProviderSyncCursorRepository/IEmailProviderClientFactory/IDistributedLock ya registrados
        // arriba, no agrega dependencias nuevas.
        services.Configure<ReconciliationOptions>(configuration.GetSection(ReconciliationOptions.SectionName));
        services.AddHostedService<ReconciliationJob>();

        services.AddScoped<IProviderConnectionAuditLogRepository, ProviderConnectionAuditLogRepository>();
        services.Configure<MessageBodyRateLimiterOptions>(configuration.GetSection("Connectors:MessageBodyRateLimit"));
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
            services.AddSingleton<IMessageBodyRateLimiter, RedisMessageBodyRateLimiter>();
        else
            services.AddSingleton<IMessageBodyRateLimiter, InMemoryMessageBodyRateLimiter>();

        services.Configure<AttachmentRateLimiterOptions>(configuration.GetSection("Connectors:AttachmentRateLimit"));
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
            services.AddSingleton<IAttachmentRateLimiter, RedisAttachmentRateLimiter>();
        else
            services.AddSingleton<IAttachmentRateLimiter, InMemoryAttachmentRateLimiter>();

        services.Configure<ConnectorsRetentionOptions>(
            configuration.GetSection(ConnectorsRetentionOptions.SectionName)
        );
        services.AddHostedService<ConnectorsRetentionScheduler>();

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

    // RateLimit Fase 2 — mismo patrón que Customer (Fase 6) y Tenant (Fase 2.1): el consumer del
    // evento de Subscription mantiene la proyección local al día incluso con el flag apagado.
    //
    // Auditoria RateLimit hallazgo #2 — Connectors ganó su primera infraestructura de token M2M
    // saliente (ver RateLimiting/ServiceTokenAcquirer.cs); antes solo RECIBIA llamadas M2M
    // (MessagesController), nunca las hacía. HttpPlanRateLimitReader ahora puede leer el
    // catálogo de Subscription, cerrando el gap documentado en Fase 2.
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
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
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceAuthClientOptions>>().Value;
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
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BuildingBlocks.Infrastructure.RateLimiting.SubscriptionClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
