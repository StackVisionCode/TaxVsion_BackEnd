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
        services.AddScoped<ISessionDenylistReader, SessionDenylistReader>();
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

        return services;
    }
}
