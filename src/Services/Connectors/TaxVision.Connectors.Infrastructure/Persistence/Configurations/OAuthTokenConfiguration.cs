using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.Persistence.Configurations;

public sealed class OAuthTokenConfiguration : IEntityTypeConfiguration<OAuthToken>
{
    public void Configure(EntityTypeBuilder<OAuthToken> builder)
    {
        builder.ToTable("OAuthTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.ConnectionId).IsRequired();
        builder.Property(t => t.AccessTokenExpiresAtUtc).IsRequired();
        builder.Property(t => t.RefreshedAtUtc).IsRequired();

        builder.OwnsOne(
            t => t.AccessTokenCipher,
            cipher =>
            {
                cipher.Property(c => c.Ciphertext).HasColumnName("AccessTokenCiphertext").IsRequired();
                cipher.Property(c => c.Nonce).HasColumnName("AccessTokenNonce").IsRequired();
                cipher.Property(c => c.Tag).HasColumnName("AccessTokenTag").IsRequired();
                cipher.Property(c => c.KeyVersion).HasColumnName("AccessTokenKeyVersion").IsRequired();
            }
        );

        builder.OwnsOne(
            t => t.RefreshTokenCipher,
            cipher =>
            {
                cipher.Property(c => c.Ciphertext).HasColumnName("RefreshTokenCiphertext").IsRequired();
                cipher.Property(c => c.Nonce).HasColumnName("RefreshTokenNonce").IsRequired();
                cipher.Property(c => c.Tag).HasColumnName("RefreshTokenTag").IsRequired();
                cipher.Property(c => c.KeyVersion).HasColumnName("RefreshTokenKeyVersion").IsRequired();
            }
        );
    }
}
