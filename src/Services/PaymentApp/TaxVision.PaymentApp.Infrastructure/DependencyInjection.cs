using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Infrastructure.Observability;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentApp.Infrastructure.Providers;
using TaxVision.PaymentApp.Infrastructure.Providers.Intellipay;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;
using TaxVision.PaymentApp.Infrastructure.Scheduling;
using TaxVision.PaymentApp.Infrastructure.Security;
using TaxVision.PaymentApp.Infrastructure.Subscriptions;

namespace TaxVision.PaymentApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentAppInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<PaymentAppDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<PaymentAppDbContext>());
        services.AddScoped<ISaaSPaymentRepository, SaaSPaymentRepository>();
        services.AddScoped<ITenantRegistry, TenantRegistry>();
        services.AddScoped<IPaymentAuditLogWriter, PaymentAuditLogWriter>();
        // RBAC Fase 6 — ISessionDenylistReader se registra en Program.cs (AddSessionDenylist vive en
        // BuildingBlocks.Web, capa que Infrastructure no debe referenciar).
        services.AddScoped<IPaymentAttemptThrottle, PaymentAttemptThrottle>();
        services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();
        services.AddScoped<ITenantProviderCustomerRepository, TenantProviderCustomerRepository>();

        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.Configure<IntellipayOptions>(configuration.GetSection(IntellipayOptions.SectionName));

        services.AddHttpClient<IntellipayGateway>();
        services.AddPaymentProviders();
        services.AddScoped<IProviderWebhookSecrets, ProviderWebhookSecrets>();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "localhost:6379")
        );
        services.AddSingleton<IDistributedLockFactory, RedisDistributedLockFactory>();

        services.AddSingleton<IPaymentAppMetrics, PaymentAppMetrics>();

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

        AddSubscriptionClient(services, configuration);
        return services;
    }

    /// <summary>PayFlow (Fase 16) — cierra el price-trust gap: resuelve el precio real de un plan
    /// vía M2M a Subscription en vez de confiar en el valor que enviaba el caller.</summary>
    private static void AddSubscriptionClient(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ServiceAuthClientOptions>().Bind(config.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddOptions<SubscriptionClientOptions>().Bind(config.GetSection(SubscriptionClientOptions.SectionName));

        services.AddHttpClient<IPaymentAppServiceTokenAcquirer, PaymentAppServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        services.AddHttpClient<ISubscriptionPlanPricingClient, SubscriptionPlanPricingClient>(
            (sp, http) =>
            {
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SubscriptionClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
