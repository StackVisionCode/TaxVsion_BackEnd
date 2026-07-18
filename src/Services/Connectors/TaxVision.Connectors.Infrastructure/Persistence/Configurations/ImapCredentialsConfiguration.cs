using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.Persistence.Configurations;

public sealed class ImapCredentialsConfiguration : IEntityTypeConfiguration<ImapCredentials>
{
    public void Configure(EntityTypeBuilder<ImapCredentials> builder)
    {
        builder.ToTable("ImapCredentials");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AccountId).IsRequired();
        builder.Property(c => c.Host).HasMaxLength(255).IsRequired();
        builder.Property(c => c.Port).IsRequired();
        builder.Property(c => c.UseSsl).IsRequired();
        builder.Property(c => c.Username).HasMaxLength(320).IsRequired();

        builder.HasIndex(c => c.AccountId).IsUnique();

        builder.OwnsOne(
            c => c.PasswordCipher,
            cipher =>
            {
                cipher.Property(c => c.Ciphertext).HasColumnName("PasswordCiphertext").IsRequired();
                cipher.Property(c => c.Nonce).HasColumnName("PasswordNonce").IsRequired();
                cipher.Property(c => c.Tag).HasColumnName("PasswordTag").IsRequired();
                cipher.Property(c => c.KeyVersion).HasColumnName("PasswordKeyVersion").IsRequired();
            }
        );
    }
}
