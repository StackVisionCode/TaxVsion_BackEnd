using Amazon.S3;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Infrastructure.Security;
using TaxVision.CloudStorage.Infrastructure.Storage;
using TaxVision.CloudStorage.Infrastructure.Time;

namespace TaxVision.CloudStorage.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCloudStorageInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<CloudStorageDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CloudStorageDbContext>());
        services.AddScoped<IFileObjectRepository, FileObjectRepository>();
        services.AddScoped<IStorageLimitRepository, StorageLimitRepository>();
        services.AddScoped<IStorageAuditRepository, StorageAuditRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IShareLinkRepository, ShareLinkRepository>();
        services.AddScoped<IDmcaNoticeRepository, DmcaNoticeRepository>();

        services.Configure<CloudStorageOptions>(configuration.GetSection(CloudStorageOptions.SectionName));
        services.Configure<MinioOptions>(configuration.GetSection(MinioOptions.SectionName));
        services.Configure<ClamAvOptions>(configuration.GetSection(ClamAvOptions.SectionName));

        services.AddSingleton<IMinioClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MinioOptions>>().Value;
            if (
                string.IsNullOrWhiteSpace(options.Endpoint)
                || string.IsNullOrWhiteSpace(options.AccessKey)
                || string.IsNullOrWhiteSpace(options.SecretKey)
            )
                throw new InvalidOperationException("MinIO endpoint and credentials are required.");

            var builder = new MinioClient()
                .WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey);
            if (options.UseTls)
                builder = builder.WithSSL();
            return builder.Build();
        });

        services.AddSingleton<IObjectStorage, MinioObjectStorage>();

        // Fase U — mismo servidor MinIO, mismas credenciales root que arriba, pero via
        // AWSSDK.S3 (el SDK "Minio" no expone publicamente los primitivos de multipart
        // presign — ver docblock de IMultipartUploadStorage).
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MinioOptions>>().Value;
            if (
                string.IsNullOrWhiteSpace(options.Endpoint)
                || string.IsNullOrWhiteSpace(options.AccessKey)
                || string.IsNullOrWhiteSpace(options.SecretKey)
            )
                throw new InvalidOperationException("MinIO endpoint and credentials are required.");

            var scheme = options.UseTls ? "https" : "http";
            var config = new AmazonS3Config
            {
                ServiceURL = $"{scheme}://{options.Endpoint}",
                ForcePathStyle = true,
                UseHttp = !options.UseTls,
            };
            return new AmazonS3Client(options.AccessKey, options.SecretKey, config);
        });
        services.AddSingleton<IMultipartUploadStorage, S3MultipartUploadStorage>();
        services.AddSingleton<IObjectKeyBuilder, DefaultObjectKeyBuilder>();
        services.AddSingleton<IVirusScanner, ClamAvVirusScanner>();
        // Moderacion de contenido (NSFW/CSAM/politica) — NoOp por defecto en este
        // MVP. Swap a una implementacion real cuando exista sin tocar el pipeline
        // de ScanFileHandler (ver docblock de IContentScanner).
        services.AddSingleton<IContentScanner, NoOpContentScanner>();
        services.AddSingleton<IFileContentInspector, FileContentInspector>();
        services.AddSingleton<IShareLinkPasswordHasher, Pbkdf2ShareLinkPasswordHasher>();
        services.AddSingleton<ISystemClock, SystemClock>();
        // Must run before Wolverine listeners so Scribe's platform layouts are not
        // dropped for a missing technical-tenant quota on a fresh database.
        services.AddHostedService<PlatformStorageLimitBootstrapper>();
        services.AddHostedService<MinioBucketBootstrapper>();
        services.AddHostedService<ExpiredUploadCleanupService>();
        services.AddHostedService<RecycleBinPurgeService>();
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
}
