using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class TenantWebhookEndpointConfiguration : IEntityTypeConfiguration<TenantWebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<TenantWebhookEndpoint> builder)
    {
        builder.ToTable("TenantWebhookEndpoints");
        builder.HasKey(endpoint => endpoint.Id);

        // *** GUARDRAIL persistencia (§49) ***
        builder.Property(endpoint => endpoint.Id).ValueGeneratedNever();

        builder.Property(endpoint => endpoint.TenantPaymentConfigId).IsRequired();
        builder.Property(endpoint => endpoint.TenantId).IsRequired();
        builder.Property(endpoint => endpoint.Url).HasMaxLength(2000).IsRequired();

        builder
            .Property(endpoint => endpoint.SigningSecret)
            .HasConversion(secret => secret.CipherText, value => EncryptedSecret.Create(value).Value)
            .HasColumnName("SigningSecretEncrypted")
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(endpoint => endpoint.IsActive).IsRequired();

        builder
            .HasIndex(endpoint => endpoint.TenantPaymentConfigId)
            .HasDatabaseName("IX_TenantWebhookEndpoints_TenantPaymentConfigId");
    }
}
