using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Infrastructure.Payments;
using TaxVision.Payment.Infrastructure.Payments.Adapters;
using TaxVision.Payment.Infrastructure.Persistence;
using TaxVision.Payment.Infrastructure.Persistence.Repositories;

namespace TaxVision.Payment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<PaymentDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IUnitOfWork>(provider =>
            provider.GetRequiredService<PaymentDbContext>());

        services.Configure<StripeOptions>(
            configuration.GetSection(StripeOptions.SectionName));

        services.AddScoped<ISaaSPaymentRepository, SaaSPaymentRepository>();
        services.AddScoped<IStripeCustomerRepository, StripeCustomerRepository>();
        services.AddScoped<ITenantPaymentConfigRepository, TenantPaymentConfigRepository>();
        services.AddScoped<ITenantTransactionRepository, TenantTransactionRepository>();

        services.AddScoped<IStripeGateway, StripeGateway>();

        services.AddSingleton<StripePaymentAdapter>();
        services.AddSingleton<PayPalPaymentAdapter>();
        services.AddSingleton<IPaymentAdapterFactory, PaymentAdapterFactory>();

        return services;
    }
}
