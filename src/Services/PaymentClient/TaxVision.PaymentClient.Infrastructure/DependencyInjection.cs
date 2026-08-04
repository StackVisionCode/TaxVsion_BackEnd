using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.RateLimiting.Abstractions;
using TaxVision.PaymentClient.Infrastructure.Observability;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Infrastructure.Providers;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;
using TaxVision.PaymentClient.Infrastructure.RateLimiting;
using TaxVision.PaymentClient.Infrastructure.Scheduling;

namespace TaxVision.PaymentClient.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentClientInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<PaymentClientDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<PaymentClientDbContext>());
        services.AddScoped<ITenantPaymentRepository, TenantPaymentRepository>();
        services.AddScoped<ITenantPaymentConfigRepository, TenantPaymentConfigRepository>();
        services.AddScoped<ITenantRegistry, TenantRegistry>();
        services.AddScoped<IPaymentAuditLogWriter, PaymentAuditLogWriter>();
        services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();
        // RBAC Fase 6 — ISessionDenylistReader se registra en Program.cs (AddSessionDenylist vive en
        // BuildingBlocks.Web, capa que Infrastructure no debe referenciar).
        services.AddScoped<IPaymentLinkRepository, PaymentLinkRepository>();
        services.AddScoped<IPayableReferenceRepository, PayableReferenceRepository>();
        services.AddScoped<ITenantConnectAccountRepository, TenantConnectAccountRepository>();
        services.AddScoped<IPayoutScheduleRepository, PayoutScheduleRepository>();
        services.AddScoped<ITenantRecurringPaymentRepository, TenantRecurringPaymentRepository>();

        services.AddPaymentProviders();
        services.AddSecretProtection();

        services.Configure<PlatformStripeCredentials>(configuration.GetSection(PlatformStripeCredentials.SectionName));
        services.AddSingleton<IStripeConnectGateway, StripeConnectGateway>();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "localhost:6379")
        );
        services.AddSingleton<IDistributedLockFactory, RedisDistributedLockFactory>();

        services.AddSingleton<IPaymentClientMetrics, PaymentClientMetrics>();

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
    // (TenantPlanCodeProjectionConsumer, mantiene la proyección al día incluso con el flag
    // apagado) y los lectores concretos de la proyección local. El mapeo a
    // BuildingBlocks.RateLimiting.ITenantPlanCodeReader/IPlanRateLimitReader que
    // RateLimitQuotaResolver realmente consume vive en Program.cs, condicional al flag
    // RateLimit:EnforceTierQuotas.
    //
    // Auditoria RateLimit hallazgo #2 — PaymentClient nunca tuvo un IServiceTokenAcquirer M2M
    // propio (es un receptor M2M puro vía InternalPayablesController, no un llamador saliente);
    // se agrega uno dedicado solo para que HttpPlanRateLimitReader pueda leer el catálogo de
    // Subscription (ver RateLimiting/ServiceTokenAcquirer.cs), cerrando el gap documentado en
    // Fase 2 — ya no cae a NullPlanRateLimitReader.
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

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
