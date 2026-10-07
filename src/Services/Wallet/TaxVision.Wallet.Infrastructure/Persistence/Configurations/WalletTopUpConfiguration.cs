using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Infrastructure.Persistence.Configurations;

public sealed class WalletTopUpConfiguration : IEntityTypeConfiguration<WalletTopUp>
{
    public void Configure(EntityTypeBuilder<WalletTopUp> builder)
    {
        builder.ToTable("WalletTopUps");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.AmountCents).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(8).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(t => t.RequestedByUserId).IsRequired();
        builder.Property(t => t.FailureReason).HasMaxLength(500);
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();
        builder.Ignore(t => t.AmountMicros); // computada

        builder.HasIndex(t => new { t.TenantId, t.IdempotencyKey }).IsUnique();
    }
}

public sealed class FundingCreditConfiguration : IEntityTypeConfiguration<FundingCredit>
{
    public void Configure(EntityTypeBuilder<FundingCredit> builder)
    {
        builder.ToTable("FundingCredits");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.SourceService).HasMaxLength(64).IsRequired();
        builder.Property(c => c.SaaSPaymentId).IsRequired();
        builder.Property(c => c.AmountMicros).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();

        // Dedupe de acreditaciones: un mismo pago nunca acredita dos veces.
        builder.HasIndex(c => new { c.SourceService, c.SaaSPaymentId }).IsUnique();
    }
}
