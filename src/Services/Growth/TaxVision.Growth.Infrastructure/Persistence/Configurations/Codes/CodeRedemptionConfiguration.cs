using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Redemptions;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Codes;

public sealed class CodeRedemptionConfiguration : IEntityTypeConfiguration<CodeRedemption>
{
    public void Configure(EntityTypeBuilder<CodeRedemption> builder)
    {
        builder.ToTable(
            "CodeRedemptions",
            GrowthSchemas.Codes,
            table =>
                table.HasCheckConstraint(
                    "CK_CodeRedemptions_Amounts",
                    "[GrossAmountCents] >= 0 AND [DiscountAmountCents] >= 0 "
                        + "AND [NetAmountCents] >= 0 "
                        + "AND [GrossAmountCents] = [DiscountAmountCents] + [NetAmountCents]"
                )
        );
        builder.HasKey(redemption => redemption.Id);

        builder.Property(redemption => redemption.TenantId).IsRequired();
        builder
            .Property(redemption => redemption.SnapshotHash)
            .HasConversion(hash => hash.Value, value => SnapshotHash.Create(value).Value)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder
            .Property(redemption => redemption.CommitIdempotencyKey)
            .HasConversion(key => key.Value, value => IdempotencyKey.Create(value).Value)
            .HasMaxLength(200)
            .IsRequired();
        builder
            .Property(redemption => redemption.CommitPayloadFingerprint)
            .HasConversion(value => value.Value, value => PayloadFingerprint.Create(value).Value)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(redemption => redemption.CommittedAtUtc).HasColumnType("datetime2(7)").IsRequired();

        builder.OwnsOne(redemption => redemption.Payment, payment => payment.ConfigurePayment());
        builder.Navigation(redemption => redemption.Payment).IsRequired();
        builder.OwnsOne(redemption => redemption.GrossAmount, money => money.ConfigureMoney("Gross"));
        builder.Navigation(redemption => redemption.GrossAmount).IsRequired();
        builder.OwnsOne(redemption => redemption.DiscountAmount, money => money.ConfigureMoney("Discount"));
        builder.Navigation(redemption => redemption.DiscountAmount).IsRequired();
        builder.OwnsOne(redemption => redemption.NetAmount, money => money.ConfigureMoney("Net"));
        builder.Navigation(redemption => redemption.NetAmount).IsRequired();

        builder
            .HasOne<CodeReservation>()
            .WithMany()
            .HasForeignKey(redemption => redemption.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<CodeDefinition>()
            .WithMany()
            .HasForeignKey(redemption => redemption.CodeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(redemption => redemption.ReservationId)
            .IsUnique()
            .HasDatabaseName("UX_CodeRedemptions_ReservationId");
    }
}
