using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Configuration;
using TaxVision.Customer.Infrastructure.Imports;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using TaxVision.Customer.Infrastructure.Security;

namespace TaxVision.Customer.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddCustomerInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<CustomerDbContext>(opt => opt.UseSqlServer(config.GetConnectionString("Default")));

        // ---- Parametros configurables de importacion (seccion "CustomerImport") ----
        // Enlazado con IOptionsMonitor para permitir cambios en caliente desde una futura
        // interfaz de administracion (config global del SaaS o por tenant).
        services
            .AddOptions<CustomerImportOptions>()
            .Bind(config.GetSection(CustomerImportOptions.SectionName))
            .Validate(o => o.MaxFileBytes > 0, "CustomerImport:MaxFileBytes must be greater than zero.");

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CustomerDbContext>());

        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerReadService, CustomerReadService>();

        services.AddSingleton<ISensitiveDataProtector, AesGcmSensitiveDataProtector>();

        // ---- Imports (bulk) ----
        services.AddScoped<ICustomerImportRepository, CustomerImportRepository>();
        services.AddScoped<ICustomerImportReadService, CustomerImportReadService>();
        services.AddScoped<IImportFileStore, SqlServerImportFileStore>();
        services.AddScoped<ICustomerDuplicateDetector, SqlServerCustomerDuplicateDetector>();
        services.AddScoped<ICatalogResolver, SqlServerCatalogResolver>();
        services.AddScoped<CsvCustomerImportReader>();
        services.AddScoped<XlsxCustomerImportReader>();
        services.AddScoped<ICustomerImportReaderFactory, CustomerImportReaderFactory>();

        // Cleanup diario (jobs > N dias). Runs como BackgroundService.
        // TODO: cuando exista RealTime Service, agregar SignalR push para notificar completado.
        services.AddHostedService<CustomerImportCleanupHostedService>();

        return services;
    }
}
