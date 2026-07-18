using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class TenantPaymentConfigConfiguration : IEntityTypeConfiguration<TenantPaymentConfig>
{
    public void Configure(EntityTypeBuilder<TenantPaymentConfig> builder)
    {
        builder.ToTable("TenantPaymentConfigs");
        builder.HasKey(config => config.Id);

        builder.Property(config => config.TenantId).IsRequired();
        builder.Property(config => config.ProviderCode).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(config => config.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(config => config.PublishableKey).HasMaxLength(500).IsRequired();

        builder
            .Property(config => config.SecretKeyEncrypted)
            .HasConversion(
                secret => secret == null ? null : secret.CipherText,
                value => value == null ? null : EncryptedSecret.Create(value).Value
            )
            .HasColumnName("SecretKeyEncrypted")
            .HasColumnType("nvarchar(max)");

        builder
            .Property(config => config.WebhookSecretEncrypted)
            .HasConversion(
                secret => secret == null ? null : secret.CipherText,
                value => value == null ? null : EncryptedSecret.Create(value).Value
            )
            .HasColumnName("WebhookSecretEncrypted")
            .HasColumnType("nvarchar(max)");

        builder
            .Property(config => config.StatementDescriptor)
            .HasConversion(descriptor => descriptor.Value, value => StatementDescriptor.Create(value).Value)
            .HasColumnName("StatementDescriptor")
            .HasMaxLength(22)
            .IsRequired();

        builder.Property(config => config.IsActive).IsRequired();

        builder
            .HasIndex(config => new { config.TenantId, config.ProviderCode })
            .IsUnique()
            .HasDatabaseName("UX_TenantPaymentConfigs_TenantId_ProviderCode");

        builder
            .HasMany(config => config.WebhookEndpoints)
            .WithOne()
            .HasForeignKey(endpoint => endpoint.TenantPaymentConfigId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(config => config.WebhookEndpoints).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
