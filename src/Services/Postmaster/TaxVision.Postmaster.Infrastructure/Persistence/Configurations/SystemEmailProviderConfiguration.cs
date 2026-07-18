using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

public sealed class SystemEmailProviderConfiguration : IEntityTypeConfiguration<SystemEmailProvider>
{
    public void Configure(EntityTypeBuilder<SystemEmailProvider> builder)
    {
        builder.ToTable("SystemEmailProviders");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.ProviderCode).HasMaxLength(50).IsRequired();
        builder.Property(p => p.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ProviderType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Host).HasMaxLength(255);
        builder.Property(p => p.Port);
        builder.Property(p => p.UseTls).IsRequired();
        builder.Property(p => p.Username).HasMaxLength(255);
        builder
            .Property(p => p.PasswordCipher)
            .HasConversion(
                secret => secret == null ? null : secret.Cipher,
                cipher => cipher == null ? null : EncryptedSecret.Create(cipher).Value
            )
            .HasColumnType("nvarchar(max)");
        builder.Property(p => p.FromAddressDefault).HasMaxLength(320).IsRequired();
        builder.Property(p => p.FromDisplayNameDefault).HasMaxLength(100);
        builder.Property(p => p.RateLimitPerMinute).IsRequired();
        builder.Property(p => p.Enabled).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc);

        builder.HasIndex(p => p.ProviderCode).IsUnique();
    }
}
