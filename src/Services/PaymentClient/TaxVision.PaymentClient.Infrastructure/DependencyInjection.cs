using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Infrastructure.Observability;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Infrastructure.Providers;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;
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
        return services;
    }
}
