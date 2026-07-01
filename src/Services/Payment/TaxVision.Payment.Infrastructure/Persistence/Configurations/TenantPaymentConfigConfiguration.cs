using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Payment.Domain.TenantPayments;

namespace TaxVision.Payment.Infrastructure.Persistence.Configurations;

public sealed class TenantPaymentConfigConfiguration : IEntityTypeConfiguration<TenantPaymentConfig>
{
    public void Configure(EntityTypeBuilder<TenantPaymentConfig> builder)
    {
        builder.ToTable("TenantPaymentConfigs");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Provider)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.PublicKey).HasMaxLength(500);
        builder.Property(c => c.SecretKeyEncrypted).HasMaxLength(2000);
        builder.Property(c => c.WebhookSecretEncrypted).HasMaxLength(2000);
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();
        builder.HasIndex(c => c.TenantId).IsUnique();
    }
}
