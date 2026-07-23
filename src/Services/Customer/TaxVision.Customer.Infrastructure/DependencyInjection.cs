using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
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
        services.AddScoped<ICustomerAuditWriter, CustomerAuditWriter>();
        services.AddScoped<ITenantEmployeeDirectoryRepository, TenantEmployeeDirectoryRepository>();

        services.AddSingleton<ISensitiveDataProtector, AesGcmSensitiveDataProtector>();

        // ---- Imports (bulk) ----
        services.AddScoped<ICustomerImportRepository, CustomerImportRepository>();
        services.AddScoped<ICustomerImportReadService, CustomerImportReadService>();
        services.AddScoped<ICustomerDuplicateDetector, SqlServerCustomerDuplicateDetector>();
        services.AddScoped<ICatalogResolver, SqlServerCatalogResolver>();
        services.AddScoped<CsvCustomerImportReader>();
        services.AddScoped<XlsxCustomerImportReader>();
        services.AddScoped<ICustomerImportReaderFactory, CustomerImportReaderFactory>();

        // Archivo de import en CloudStorage (reemplaza CustomerImportFiles/IImportFileStore) —
        // sube directo a MinIO con credenciales propias; descarga/borrado via HTTP+M2M.
        services.AddOptions<ServiceAuthClientOptions>().Bind(config.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddOptions<CloudStorageClientOptions>().Bind(config.GetSection(CloudStorageClientOptions.SectionName));
        services.AddOptions<CustomerMinioOptions>().Bind(config.GetSection(CustomerMinioOptions.SectionName));

        services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
            }
        );

        services.AddSingleton<IMinioClient>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<CustomerMinioOptions>>().Value;
            var builder = new MinioClient().WithEndpoint(opt.Endpoint).WithCredentials(opt.AccessKey, opt.SecretKey);
            if (opt.UseTls)
                builder = builder.WithSSL();
            return builder.Build();
        });

        services.AddHttpClient<ICustomerImportCloudStorageClient, CustomerImportCloudStorageClient>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
            }
        );

        // Cleanup diario (jobs > N dias). Runs como BackgroundService.
        // TODO: cuando exista RealTime Service, agregar SignalR push para notificar completado.
        services.AddHostedService<CustomerImportCleanupHostedService>();

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

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
