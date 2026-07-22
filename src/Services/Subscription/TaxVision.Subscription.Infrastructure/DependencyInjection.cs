using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Infrastructure.Growth;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Infrastructure.Scheduling;

namespace TaxVision.Subscription.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSubscriptionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<SubscriptionDbContext>(options => options.UseSqlServer(connectionString));
        services.Configure<SubscriptionOptions>(configuration.GetSection(SubscriptionOptions.SectionName));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<SubscriptionDbContext>());
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ISubscriptionRepository, TenantSubscriptionRepository>();
        services.AddScoped<ISubscriptionSeatRepository, SubscriptionSeatRepository>();
        services.AddScoped<ISubscriptionTenantSettingsRepository, SubscriptionTenantSettingsRepository>();
        services.AddScoped<IAddOnDefinitionRepository, AddOnDefinitionRepository>();
        services.AddScoped<ITenantAddOnRepository, TenantAddOnRepository>();
        services.AddScoped<ITenantEntitlementSnapshotRepository, TenantEntitlementSnapshotRepository>();
        services.AddScoped<ISubscriptionAuditLogWriter, SubscriptionAuditLogWriter>();
        services.AddScoped<ISubscriptionAuditLogRepository, SubscriptionAuditLogRepository>();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis") ?? "localhost:6379")
        );
        services.AddSingleton<IDistributedLockFactory, RedisDistributedLockFactory>();

        // Fase 4 Referidos (2026-07-21) — M2M contra Growth para reservar el descuento de
        // bienvenida del referido antes de la primera activación. Mismo patrón que
        // CorrespondenceServiceTokenAcquirer/CorrespondenceCustomerClient.
        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddOptions<GrowthClientOptions>().Bind(configuration.GetSection(GrowthClientOptions.SectionName));

        services.AddHttpClient<IGrowthServiceTokenAcquirer, GrowthServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
        services.AddHttpClient<IReferralBenefitReserver, GrowthRefereeBenefitClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<GrowthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        return services;
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
