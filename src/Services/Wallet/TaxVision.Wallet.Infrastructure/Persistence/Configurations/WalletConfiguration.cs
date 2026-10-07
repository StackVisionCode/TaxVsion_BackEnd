using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TaxVision.Wallet.Infrastructure.Persistence.Configurations;

public sealed class WalletConfiguration : IEntityTypeConfiguration<Domain.Wallet.Wallet>
{
    public void Configure(EntityTypeBuilder<Domain.Wallet.Wallet> builder)
    {
        // Invariante del modelo v3 defendida en la BD: Posted >= Held >= 0.
        builder.ToTable(
            "Wallets",
            t => t.HasCheckConstraint("CK_Wallets_Balances", "[PostedMicros] >= [HeldMicros] AND [HeldMicros] >= 0")
        );
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.TenantId).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(8).IsRequired();
        builder.Property(w => w.PostedMicros).IsRequired();
        builder.Property(w => w.HeldMicros).IsRequired();
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();
        builder.Property(w => w.RowVersion).IsRowVersion();
        builder.Ignore(w => w.AvailableMicros); // computada: Posted − Held

        // Un monedero por (tenant, moneda).
        builder.HasIndex(w => new { w.TenantId, w.Currency }).IsUnique();
    }
}
