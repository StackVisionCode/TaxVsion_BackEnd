using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Configurations;

public sealed class FileObjectConfiguration : IEntityTypeConfiguration<FileObject>
{
    public void Configure(EntityTypeBuilder<FileObject> builder)
    {
        builder.ToTable("Files");
        builder.HasKey(file => file.Id);
        builder.Property(file => file.TenantId).IsRequired();
        builder.Property(file => file.OwnerType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(file => file.FolderType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(file => file.ObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(file => file.OriginalName).HasMaxLength(255).IsRequired();
        builder.Property(file => file.DeclaredContentType).HasMaxLength(128).IsRequired();
        builder.Property(file => file.DetectedContentType).HasMaxLength(128);
        builder.Property(file => file.ChecksumSha256).HasMaxLength(64);
        builder.Property(file => file.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(file => file.ScanReport).HasMaxLength(2048);
        builder.HasIndex(file => new { file.TenantId, file.Id });
        builder.HasIndex(file => new { file.TenantId, file.ObjectKey }).IsUnique();
        builder.HasIndex(file => new
        {
            file.TenantId,
            file.Status,
            file.CreatedAtUtc,
        });
    }
}

public sealed class TenantStorageLimitConfiguration : IEntityTypeConfiguration<TenantStorageLimit>
{
    public void Configure(EntityTypeBuilder<TenantStorageLimit> builder)
    {
        builder.ToTable("TenantStorageLimits");
        builder.HasKey(limit => limit.Id);
        builder.Property(limit => limit.TenantId).IsRequired();
        builder.Property(limit => limit.PlanCode).HasMaxLength(64).IsRequired();
        builder.Property(limit => limit.RowVersion).IsRowVersion();
        builder.HasIndex(limit => limit.TenantId).IsUnique();
    }
}

public sealed class StorageAccessLogConfiguration : IEntityTypeConfiguration<StorageAccessLog>
{
    public void Configure(EntityTypeBuilder<StorageAccessLog> builder)
    {
        builder.ToTable("StorageAccessLogs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.TenantId).IsRequired();
        builder.Property(log => log.Action).HasMaxLength(64).IsRequired();
        builder.Property(log => log.Outcome).HasMaxLength(32).IsRequired();
        builder.Property(log => log.IpAddress).HasMaxLength(64);
        builder.Property(log => log.UserAgent).HasMaxLength(512);
        builder.Property(log => log.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(log => log.Details).HasMaxLength(2048);
        builder.HasIndex(log => new { log.TenantId, log.OccurredAtUtc });
        builder.HasIndex(log => new
        {
            log.TenantId,
            log.FileId,
            log.OccurredAtUtc,
        });
        builder.HasIndex(log => new
        {
            log.TenantId,
            log.ActorId,
            log.OccurredAtUtc,
        });
    }
}
