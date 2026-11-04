using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.CloudStorage.Domain.Platform;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Configurations;

// Fila única de la plataforma (sin tenant); la migración siembra StoragePlatformSettings.Defaults().
public sealed class StoragePlatformSettingsConfiguration : IEntityTypeConfiguration<StoragePlatformSettings>
{
    public void Configure(EntityTypeBuilder<StoragePlatformSettings> builder)
    {
        builder.ToTable("StoragePlatformSettings");
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();
        builder.Property(settings => settings.SignedUrlMinutes).IsRequired();
        builder.Property(settings => settings.ShareUrlMinutes).IsRequired();
        builder.Property(settings => settings.UploadReservationHours).IsRequired();
        builder.Property(settings => settings.RecycleBinRetentionDays).IsRequired();
        builder.Property(settings => settings.MaxZipFiles).IsRequired();
        builder.Property(settings => settings.MaxZipFolders).IsRequired();
        builder.Property(settings => settings.MaxZipAggregateBytes).IsRequired();
        builder.Property(settings => settings.SharePasswordLockMinutes).IsRequired();
        builder.Property(settings => settings.UpdatedAtUtc).IsRequired();
    }
}
