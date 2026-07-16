using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.PaymentLinks;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class PaymentLinkConfiguration : IEntityTypeConfiguration<PaymentLink>
{
    public void Configure(EntityTypeBuilder<PaymentLink> builder)
    {
        builder.ToTable("PaymentLinks");
        builder.HasKey(link => link.Id);

        builder.Property(link => link.TenantId).IsRequired();
        builder.Property(link => link.TaxpayerId);

        builder.OwnsOne(link => link.Amount, money =>
        {
            money.Property(m => m.AmountCents).HasColumnName("AmountCents").IsRequired();
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(link => link.Purpose, purpose =>
        {
            purpose.Property(p => p.Kind).HasColumnName("PurposeKind").HasConversion<string>().HasMaxLength(30).IsRequired();
            purpose.Property(p => p.ExternalReferenceId).HasColumnName("PurposeExternalReferenceId").HasMaxLength(200);
        });

        builder.Property(link => link.Token)
            .HasConversion(token => token.Value, value => PaymentLinkToken.FromExisting(value).Value)
            .HasColumnName("Token")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(link => link.Token).IsUnique().HasDatabaseName("UX_PaymentLinks_Token");

        builder.Property(link => link.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(link => link.ExpiresAtUtc).IsRequired();
        builder.Property(link => link.CreatedAtUtc).IsRequired();
        builder.Property(link => link.UsedAtUtc);
        builder.Property(link => link.RelatedTenantPaymentId);
        builder.Property(link => link.CreatedBy).IsRequired();
        builder.Property(link => link.FailedRedemptionAttempts).IsRequired().HasDefaultValue(0);

        builder.HasIndex(link => new { link.TenantId, link.Status })
            .HasDatabaseName("IX_PaymentLinks_TenantId_Status");

        // Usado por PaymentLinkExpirationJob: barrer todo lo Active con ExpiresAtUtc vencido.
        builder.HasIndex(link => link.ExpiresAtUtc)
            .HasFilter("[Status] = 'Active'")
            .HasDatabaseName("IX_PaymentLinks_Status_ExpiresAtUtc");
    }
}
