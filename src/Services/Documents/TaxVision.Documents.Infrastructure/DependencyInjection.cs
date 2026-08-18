using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Application.RateLimiting.Abstractions;
using TaxVision.Documents.Infrastructure.Observability;
using TaxVision.Documents.Infrastructure.Persistence;
using TaxVision.Documents.Infrastructure.Persistence.Repositories;
using TaxVision.Documents.Infrastructure.PlatformIssuer;
using TaxVision.Documents.Infrastructure.RateLimiting;
using TaxVision.Documents.Infrastructure.Rendering;
using TaxVision.Documents.Infrastructure.Storage;

namespace TaxVision.Documents.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDocumentsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<DocumentsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<DocumentsDbContext>());
        services.AddScoped<IDocumentGenerationRepository, DocumentGenerationRepository>();
        services.AddScoped<IDocumentBrandingRepository, DocumentBrandingRepository>();

        // RBAC Fase 7 — proyección local de permisos. El repo de usuario se resuelve bajo dos puertos
        // (la MISMA instancia scoped): el rico para los consumers y el angosto que consulta
        // ProjectionPermissionsSource (BuildingBlocks.Permissions.IUserPermissionsProjectionReader).
        services.AddScoped<AuthzUserPermissionsProjectionRepository>();
        services.AddScoped<IAuthzUserPermissionsProjectionRepository>(sp =>
            sp.GetRequiredService<AuthzUserPermissionsProjectionRepository>()
        );
        services.AddScoped<BuildingBlocks.Permissions.IUserPermissionsProjectionReader>(sp =>
            sp.GetRequiredService<AuthzUserPermissionsProjectionRepository>()
        );
        services.AddScoped<IAuthzRolePermissionsProjectionRepository, AuthzRolePermissionsProjectionRepository>();
        services.AddScoped<IDocumentTemplateRenderer, TemplateDocumentRenderer>();
        services.AddSingleton<IHtmlToPdfConverter, PlaywrightHtmlToPdfConverter>();
        services.AddSingleton<IQrCodeGenerator, QrCoderQrGenerator>();
        services.AddScoped<IDocumentStorageClient, DocumentStorageClient>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<DocumentsMetrics>();

        // PayFlow (Fase 10) — datos fijos del emisor plataforma para el recibo de onboarding.
        services.AddSingleton<IPlatformIssuerProvider, PlatformIssuerProvider>();

        services.AddOptions<DocumentsPdfOptions>().Bind(configuration.GetSection(DocumentsPdfOptions.SectionName));
        services.AddOptions<DocumentsMinioOptions>().Bind(configuration.GetSection(DocumentsMinioOptions.SectionName));
        services.AddOptions<PlatformIssuerOptions>().Bind(configuration.GetSection(PlatformIssuerOptions.SectionName));

        // Cliente MinIO propio de Documents — credenciales scoped (IAM documents-source), nunca las root
        // de CloudStorage. Solo para el PUT del archivo generado al bucket temporal.
        services.AddSingleton<IMinioClient>(sp =>
        {
            var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DocumentsMinioOptions>>().Value;
            var minio = new MinioClient().WithEndpoint(opt.Endpoint).WithCredentials(opt.AccessKey, opt.SecretKey);
            if (opt.UseTls)
                minio = minio.WithSSL();
            return minio.Build();
        });

        AddRateLimitTierQuotas(services, configuration);
        return services;
    }

    // RateLimit Fase 2 — piezas siempre registradas: el consumer del evento de Subscription
    // (TenantPlanCodeProjectionConsumer, mantiene la proyección al día incluso con el flag
    // apagado) y los lectores concretos de la proyección local. El mapeo a
    // BuildingBlocks.RateLimiting.ITenantPlanCodeReader/IPlanRateLimitReader que
    // RateLimitQuotaResolver realmente consume vive en Program.cs, condicional al flag
    // RateLimit:EnforceTierQuotas.
    //
    // Auditoria RateLimit hallazgo #2 — Documents nunca tuvo un IServiceTokenAcquirer M2M
    // propio (es un servicio de recursos al que los demás llaman, no un llamador); se agrega
    // uno dedicado solo para que HttpPlanRateLimitReader pueda leer el catálogo de
    // Subscription (ver RateLimiting/ServiceTokenAcquirer.cs), cerrando el gap documentado en
    // Fase 2 — ya no cae a NullPlanRateLimitReader.
    private static void AddRateLimitTierQuotas(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantPlanCodeProjectionRepository, TenantPlanCodeProjectionRepository>();
        services.AddScoped<EfTenantPlanCodeReader>();
        services.AddScoped<BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader>(
            sp => new BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader(
                sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
                sp.GetRequiredService<EfTenantPlanCodeReader>()
            )
        );
        services.AddScoped<
            BuildingBlocks.RateLimiting.ITenantPlanCodeCacheInvalidator,
            TenantPlanCodeCacheInvalidator
        >();

        services
            .AddOptions<ServiceAuthClientOptions>()
            .Bind(configuration.GetSection(ServiceAuthClientOptions.SectionName));
        services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
            }
        );

        services
            .AddOptions<SubscriptionClientOptions>()
            .Bind(configuration.GetSection(SubscriptionClientOptions.SectionName));
        services.AddHttpClient<HttpPlanRateLimitReader>(
            (sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<SubscriptionClientOptions>>().Value;
                http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
                http.Timeout = TimeSpan.FromSeconds(30);
            }
        );
    }

    private static string NormalizeBaseUrl(string url) => url.EndsWith('/') ? url : url + "/";
}
