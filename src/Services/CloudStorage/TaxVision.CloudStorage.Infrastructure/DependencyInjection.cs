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
        services.AddSingleton<IObjectKeyBuilder, DefaultObjectKeyBuilder>();
        services.AddSingleton<IVirusScanner, ClamAvVirusScanner>();
        services.AddSingleton<IFileContentInspector, FileContentInspector>();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddHostedService<MinioBucketBootstrapper>();
        services.AddHostedService<ExpiredUploadCleanupService>();
        return services;
    }
}
