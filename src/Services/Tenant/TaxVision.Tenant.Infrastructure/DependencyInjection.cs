using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Infrastructure.Branding;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tenant.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddTenantInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<TenantDbContext>(opt => opt.UseSqlServer(config.GetConnectionString("Default")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TenantDbContext>());

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantReadService, TenantReadService>();

        AddBranding(services, config);

        return services;
    }

    /// <summary>Cliente de CloudStorage para el logo del tenant — ver ITenantBrandingCloudStorageClient.</summary>
    private static void AddBranding(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ServiceAuthClientOptions>().Bind(config.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddOptions<CloudStorageClientOptions>().Bind(config.GetSection(CloudStorageClientOptions.SectionName));
        services.AddOptions<TenantMinioOptions>().Bind(config.GetSection(TenantMinioOptions.SectionName));

        // Timeout 30s fijo — mismo valor que Postmaster/Correspondence/Connectors ya usan en sus
        // clientes M2M salientes (hardening Fase 13/3), para que una caída de Auth/CloudStorage
        // no cuelgue GetTenantLogo/RemoveTenantLogo hasta el default de 100s de HttpClient.
        services.AddHttpClient<ITenantServiceTokenAcquirer, TenantServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        // IAM propia de Tenant (taxvision-temp/tenant-branding/*), nunca las credenciales root de
        // CloudStorage — mismo criterio que Signature/Customer (Fase D1).
        services.AddSingleton<IMinioClient>(sp =>
        {
            var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TenantMinioOptions>>().Value;
            var builder = new MinioClient().WithEndpoint(opt.Endpoint).WithCredentials(opt.AccessKey, opt.SecretKey);
            if (opt.UseTls)
                builder = builder.WithSSL();
            return builder.Build();
        });

        services.AddHttpClient<ITenantBrandingCloudStorageClient, TenantBrandingCloudStorageClient>(
            (sp, http) =>
            {
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CloudStorageClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
