using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.ReadServices;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;

namespace TaxVision.Subscription.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddSubscriptionInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddDbContext<SubscriptionDbContext>(opt =>
            opt.UseSqlServer(config.GetConnectionString("Default")));

        services.AddScoped<IUnitOfWork>(sp =>
            sp.GetRequiredService<SubscriptionDbContext>());

        // Repositories
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IModuleRepository, ModuleRepository>();
        services.AddScoped<