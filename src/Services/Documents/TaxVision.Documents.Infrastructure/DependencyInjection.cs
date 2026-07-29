using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Infrastructure.Observability;
using TaxVision.Documents.Infrastructure.Persistence;
using TaxVision.Documents.Infrastructure.Persistence.Repositories;
using TaxVision.Documents.Infrastructure.PlatformIssuer;
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

        return services;
    }
}
