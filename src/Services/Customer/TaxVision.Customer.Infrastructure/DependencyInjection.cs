using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using TaxVision.Customer.Infrastructure.Security;

namespace TaxVision.Customer.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddCustomerInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<CustomerDbContext>(opt => opt.UseSqlServer(config.GetConnectionString("Default")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CustomerDbContext>());

        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerReadService, CustomerReadService>();

        services.AddSingleton<ISensitiveDataProtector, AesGcmSensitiveDataProtector>();

        return services;
    }
}
