using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Wallet.Domain.Settings;

namespace TaxVision.Wallet.Infrastructure.Persistence.Configurations;

public sealed class WalletSettingsConfiguration : IEntityTypeConfiguration<WalletSettings>
{
    public void Configure(EntityTypeBuilder<WalletSettings> builder)
    {
        builder.ToTable("WalletSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.LowBalanceThresholdMicros).IsRequired();
        builder.Property(s => s.LowBalanceAlertSent).IsRequired();
        builder.Property(s => s.AutoRechargeStatus).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(s => s.AutoRechargePausedReason).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.AutoRechargeProvider).HasMaxLength(AutoRechargeMethod.MaxProviderLength);
        builder.Property(s => s.AutoRechargeMethodReference).HasMaxLength(AutoRechargeMethod.MaxReferenceLength);
        builder.Property(s => s.AutoRechargeMethodLabel).HasMaxLength(AutoRechargeMethod.MaxLabelLength);
        builder.Property(s => s.ConsentIpAddress).HasMaxLength(64);
        builder.Property(s => s.ConsentTextVersion).HasMaxLength(32);
        builder.Property(s => s.ConsentText).HasMaxLength(2000);
        builder.Property(s => s.UpdatedAtUtc).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Ignore(s => s.AuthorizedMethod);

        // Uno por oficina.
        builder.HasIndex(s => s.TenantId).IsUnique();
    }
}

public sealed class WalletSettingsChangeConfiguration : IEntityTypeConfiguration<WalletSettingsChange>
{
    public void Configure(EntityTypeBuilder<WalletSettingsChange> builder)
    {
        builder.ToTable("WalletSettingsChanges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.AtUtc).IsRequired();
        builder.Property(c => c.Action).HasConversion<string>().HasMaxLength(48).IsRequired();
        builder.Property(c => c.Detail).HasMaxLength(WalletSettingsChange.MaxDetailLength).IsRequired();
        builder.Property(c => c.ConsentTextVersion).HasMaxLength(32);
        builder.Property(c => c.ConsentText).HasMaxLength(2000);
        builder.Property(c => c.IpAddress).HasMaxLength(64);

        builder.HasIndex(c => new { c.TenantId, c.AtUtc });
    }
}
