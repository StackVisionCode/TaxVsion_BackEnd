using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;

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

        return services;
    }
}
