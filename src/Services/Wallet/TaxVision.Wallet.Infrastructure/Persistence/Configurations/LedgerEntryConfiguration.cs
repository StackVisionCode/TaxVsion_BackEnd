using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Infrastructure.Persistence.Configurations;

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever(); // Id asignado en dominio (guardrail 10)
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Movement).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.DeltaPostedMicros).IsRequired();
        builder.Property(e => e.DeltaHeldMicros).IsRequired();
        builder.Property(e => e.PostedAfterMicros).IsRequired();
        builder.Property(e => e.HeldAfterMicros).IsRequired();
        builder.Property(e => e.OperationKey).HasMaxLength(200).IsRequired();
        builder.Property(e => e.CreatedAtUtc).IsRequired();

        // Idempotencia económica: un reintento con la misma OperationKey no duplica el asiento.
        builder.HasIndex(e => new { e.TenantId, e.OperationKey }).IsUnique();
        // Listado del ledger (más recientes primero) por tenant.
        builder.HasIndex(e => new { e.TenantId, e.CreatedAtUtc });
    }
}
