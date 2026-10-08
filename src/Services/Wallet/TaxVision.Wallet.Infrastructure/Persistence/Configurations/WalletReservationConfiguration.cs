using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Wallet.Domain.Reservations;

namespace TaxVision.Wallet.Infrastructure.Persistence.Configurations;

public sealed class WalletReservationConfiguration : IEntityTypeConfiguration<WalletReservation>
{
    public void Configure(EntityTypeBuilder<WalletReservation> builder)
    {
        builder.ToTable("WalletReservations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever(); // Id asignado en dominio (guardrail 10)
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.ReferenceType).HasMaxLength(64).IsRequired();
        builder.Property(r => r.ReferenceId).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.ReservedMicros).IsRequired();
        builder.Property(r => r.ReservedUnits).IsRequired();
        builder.Property(r => r.PriceBookVersion).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.ConsumedMicros).IsRequired();
        builder.Property(r => r.ReleasedMicros).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();

        // Idempotencia de la reserva: una sola por referencia (cualquier consumidor).
        builder
            .HasIndex(r => new
            {
                r.TenantId,
                r.ReferenceType,
                r.ReferenceId,
            })
            .IsUnique();
    }
}
