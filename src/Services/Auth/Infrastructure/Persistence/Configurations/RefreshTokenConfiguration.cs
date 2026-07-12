using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.TenantId).IsRequired();
        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.RevokedReason).HasMaxLength(64);
        builder.Property(token => token.ExpiresAtUtc).IsRequired();
        builder.Property(token => token.CreatedAtUtc).IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.UserId);
        builder.HasIndex(token => token.SessionId);
        builder.Ignore(token => token.IsActive);
        builder.Ignore(token => token.WasRotated);

        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);

        // Sin cascade con la sesión para evitar rutas de borrado múltiples con User.
        builder
            .HasOne<UserSession>()
            .WithMany()
            .HasForeignKey(token => token.SessionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
