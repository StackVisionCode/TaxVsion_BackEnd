using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Codes.Domain.Compensations;
using TaxVision.Codes.Domain.Redemptions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Codes;

public sealed class CodeCompensationConfiguration : IEntityTypeConfiguration<CodeCompensation>
{
    public void Configure(EntityTypeBuilder<CodeCompensation> builder)
    {
        builder.ToTable(
            "CodeCompensations",
            GrowthSchemas.Codes,
            table =>
                table.HasCheckConstraint(
                    "CK_CodeCompensations_Adjustment",
                    "[AdjustmentAmountCents] >= 0 AND [CumulativeAdjustmentAmountCents] >= 0"
                )
        );
        builder.HasKey(compensation => compensation.Id);

        builder.Property(compensation => compensation.TenantId).IsRequired();
        builder.Property(compensation => compensation.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(compensation => compensation.CumulativeAdjustmentAmountCents).IsRequired();
        builder.Property(compensation => compensation.IsFinal).IsRequired();
        builder.Property(compensation => compensation.Reason).HasMaxLength(500).IsRequired();
        builder
            .Property(compensation => compensation.IdempotencyKey)
            .HasConversion(key => key.Value, value => IdempotencyKey.Create(value).Value)
            .HasMaxLength(200)
            .IsRequired();
        builder
            .Property(compensation => compensation.PayloadFingerprint)
            .HasConversion(value => value.Value, value => PayloadFingerprint.Create(value).Value)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(compensation => compensation.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();

        builder.OwnsOne(
            compensation => compensation.AdjustmentAmount,
            amount => amount.ConfigureMoney("Adjustment")
        );
        builder.Navigation(compensation => compensation.AdjustmentAmount).IsRequired();

        builder
            .HasOne<CodeRedemption>()
            .WithMany()
            .HasForeignKey(compensation => compensation.RedemptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(compensation => new
            {
                compensation.TenantId,
                compensation.RedemptionId,
                compensation.SourceEventId,
            })
            .IsUnique()
            .HasDatabaseName("UX_CodeCompensations_Tenant_Redemption_Event");
        builder
            .HasIndex(compensation => new { compensation.TenantId, compensation.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_CodeCompensations_TenantId_IdempotencyKey");
    }
}
