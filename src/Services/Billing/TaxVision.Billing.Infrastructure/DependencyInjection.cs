using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Infrastructure.Observability;
using TaxVision.Billing.Infrastructure.Persistence;
using TaxVision.Billing.Infrastructure.Persistence.Repositories;

namespace TaxVision.Billing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<BillingDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<BillingDbContext>());
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IPaymentReceiptRepository, PaymentReceiptRepository>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<BillingMetrics>();

        return services;
    }
}
