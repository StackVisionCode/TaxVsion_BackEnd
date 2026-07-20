using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Quotes;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Codes;

public sealed class CodeReservationConfiguration : IEntityTypeConfiguration<CodeReservation>
{
    public void Configure(EntityTypeBuilder<CodeReservation> builder)
    {
        builder.ToTable(
            "CodeReservations",
            GrowthSchemas.Codes,
            table =>
                table.HasCheckConstraint(
                    "CK_CodeReservations_Amounts",
                    "[GrossAmountCents] >= 0 AND [DiscountAmountCents] >= 0 "
                        + "AND [NetAmountCents] >= 0 "
                        + "AND [GrossAmountCents] = [DiscountAmountCents] + [NetAmountCents]"
                )
        );
        builder.HasKey(reservation => reservation.Id);
        builder.Ignore(reservation => reservation.DomainEvents);

        builder.Property(reservation => reservation.TenantId).IsRequired();
        builder
            .Property(reservation => reservation.SnapshotHash)
            .HasConversion(hash => hash.Value, value => SnapshotHash.Create(value).Value)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder
            .Property(reservation => reservation.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        ConfigureRequiredKey(
            builder.Property(reservation => reservation.ReservationIdempotencyKey),
            "ReservationIdempotencyKey"
        );
        ConfigureRequiredFingerprint(
            builder.Property(reservation => reservation.ReservationPayloadFingerprint),
            "ReservationPayloadFingerprint"
        );
        ConfigureKey(builder.Property(reservation => reservation.CommitIdempotencyKey), "CommitIdempotencyKey");
        ConfigureFingerprint(
            builder.Property(reservation => reservation.CommitPayloadFingerprint),
            "CommitPayloadFingerprint"
        );
        ConfigureKey(
            builder.Property(reservation => reservation.CancellationIdempotencyKey),
            "CancellationIdempotencyKey"
        );
        ConfigureFingerprint(
            builder.Property(reservation => reservation.CancellationPayloadFingerprint),
            "CancellationPayloadFingerprint"
        );
        builder.Property(reservation => reservation.CancellationReason).HasMaxLength(500);
        builder.Property(reservation => reservation.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(reservation => reservation.ExpiresAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(reservation => reservation.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(reservation => reservation.CommittedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(reservation => reservation.CancelledAtUtc).HasColumnType("datetime2(7)");
        builder.Property(reservation => reservation.ExpiredAtUtc).HasColumnType("datetime2(7)");
        builder.Property(reservation => reservation.CompensatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(reservation => reservation.RowVersion).IsRowVersion();

        builder.OwnsOne(
            reservation => reservation.Payment,
            payment =>
            {
                payment.ConfigurePayment();
                payment
                    .HasIndex(value => new { value.Source, value.PaymentId })
                    .IsUnique()
                    .HasDatabaseName("UX_CodeReservations_Payment");
            }
        );
        builder.Navigation(reservation => reservation.Payment).IsRequired();
        builder.OwnsOne(reservation => reservation.Subject, subject => subject.ConfigureSubject());
        builder.Navigation(reservation => reservation.Subject).IsRequired();
        builder.OwnsOne(reservation => reservation.GrossAmount, money => money.ConfigureMoney("Gross"));
        builder.Navigation(reservation => reservation.GrossAmount).IsRequired();
        builder.OwnsOne(reservation => reservation.DiscountAmount, money => money.ConfigureMoney("Discount"));
        builder.Navigation(reservation => reservation.DiscountAmount).IsRequired();
        builder.OwnsOne(reservation => reservation.NetAmount, money => money.ConfigureMoney("Net"));
        builder.Navigation(reservation => reservation.NetAmount).IsRequired();

        builder
            .HasOne<CodeQuote>()
            .WithMany()
            .HasForeignKey(reservation => reservation.QuoteId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<CodeDefinition>()
            .WithMany()
            .HasForeignKey(reservation => reservation.CodeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(reservation => new
            {
                reservation.TenantId,
                reservation.ReservationIdempotencyKey,
            })
            .IsUnique()
            .HasDatabaseName("UX_CodeReservations_TenantId_IdempotencyKey");
        builder
            .HasIndex(reservation => new { reservation.Status, reservation.ExpiresAtUtc })
            .HasDatabaseName("IX_CodeReservations_Status_ExpiresAtUtc");
    }

    private static void ConfigureKey(
        PropertyBuilder<IdempotencyKey?> property,
        string columnName
    ) =>
        property
            .HasConversion(
                value => value == null ? null : value.Value,
                value => value == null ? null : IdempotencyKey.Create(value).Value
            )
            .HasColumnName(columnName)
            .HasMaxLength(200);

    private static void ConfigureRequiredKey(
        PropertyBuilder<IdempotencyKey> property,
        string columnName
    ) =>
        property
            .HasConversion(
                value => value.Value,
                value => IdempotencyKey.Create(value).Value
            )
            .HasColumnName(columnName)
            .HasMaxLength(200)
            .IsRequired();

    private static void ConfigureFingerprint(
        PropertyBuilder<PayloadFingerprint?> property,
        string columnName
    ) =>
        property
            .HasConversion(
                value => value == null ? null : value.Value,
                value => value == null ? null : PayloadFingerprint.Create(value).Value
            )
            .HasColumnName(columnName)
            .HasMaxLength(64)
            .IsFixedLength();

    private static void ConfigureRequiredFingerprint(
        PropertyBuilder<PayloadFingerprint> property,
        string columnName
    ) =>
        property
            .HasConversion(
                value => value.Value,
                value => PayloadFingerprint.Create(value).Value
            )
            .HasColumnName(columnName)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
}
