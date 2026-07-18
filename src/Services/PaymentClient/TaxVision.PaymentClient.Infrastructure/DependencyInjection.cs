using BuildingBlocks.Infrastructure.Security;
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
using TaxVision.PaymentClient.Infrastructure.Security;

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
        services.AddScoped<ISessionDenylistReader, SessionDenylistReader>();
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

        return services;
    }
}
