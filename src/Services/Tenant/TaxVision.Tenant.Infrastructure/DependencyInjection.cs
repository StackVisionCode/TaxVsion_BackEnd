using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using TaxVision.Tenant.Application.Abstractions;
using TaxVision.Tenant.Application.RateLimiting.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Infrastructure.Branding;
using TaxVision.Tenant.Infrastructure.Onboarding;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Persistence.Repositories;
using TaxVision.Tenant.Infrastructure.RateLimiting;

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

        AddRateLimitTierQuotas(services, config);
        return services;
    }

    // RateLimit Fase 2 — piezas siempre registradas: el consumer del evento de Subscription
    // (mantiene la proyección al día incluso con el flag apagado) y los lectores concretos. El
    // mapeo a ITenantPlanCodeReader/IPlanRateLimitReader (los que RateLimitQuotaResolver
    // realmente consume) es condicional al flag RateLimit:EnforceTierQuotas — decidido en
    // Program.cs, ANTES de AddTieredRateLimiting().
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration config)
    {
        // HttpPlanRateLimitReader (BuildingBlocks.Infrastructure.RateLimiting) depende del
        // contrato compartido; TenantServiceTokenAcquirer ya lo implementa (ver Branding), solo
        // falta el forwarding.
        services.AddTransient<BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer>(sp =>
            (BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer)
                sp.GetRequiredService<ITenantServiceTokenAcquirer>()
        );

        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped<CachedTenantPlanCodeReader>(sp => new CachedTenantPlanCodeReader(
            sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
            sp.GetRequiredService<EfTenantPlanCodeReader>()
        ));
        services.AddScoped<
            BuildingBlocks.RateLimiting.ITenantPlanCodeCacheInvalidator,
            TenantPlanCodeCacheInvalidator
        >();

        services.AddOptions<SubscriptionClientOptions>().Bind(config.GetSection(SubscriptionClientOptions.SectionName));
        services.AddHttpClient<HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
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

        // Fase 18 — mismo AuthBaseUrl que ITenantServiceTokenAcquirer.
        services.AddHttpClient<IAuthInvitationTokenReferenceClient, AuthInvitationTokenReferenceClient>(
            (sp, http) =>
            {
                var opt =
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
