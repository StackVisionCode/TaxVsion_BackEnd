using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo EF Core de PasswordResetToken: tabla, índice único por hash y relación en cascada con el usuario.</summary>
public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("PasswordResetTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.TenantId).IsRequired();
        builder.Property(token => token.UserId).IsRequired();
        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.RequestedIp).HasMaxLength(45);
        builder.Property(token => token.CreatedAtUtc).IsRequired();
        builder.Property(token => token.ExpiresAtUtc).IsRequired();

        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.UsedAtUtc });

        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapeo EF Core de EmailVerificationToken: tabla, índice único por hash y relación en cascada con el usuario.</summary>
public sealed class EmailVerificationTokenConfiguration : IEntityTypeConfiguration<EmailVerificationToken>
{
    public void Configure(EntityTypeBuilder<EmailVerificationToken> builder)
    {
        builder.ToTable("EmailVerificationTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.TenantId).IsRequired();
        builder.Property(token => token.UserId).IsRequired();
        builder.Property(token => token.NewEmail).HasMaxLength(320).IsRequired();
        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.CreatedAtUtc).IsRequired();
        builder.Property(token => token.ExpiresAtUtc).IsRequired();

        builder.HasIndex(token => token.TokenHash).IsUnique();

        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapeo EF Core de PhoneVerificationToken: tabla, índice por usuario/uso y relación en cascada con el usuario.</summary>
public sealed class PhoneVerificationTokenConfiguration : IEntityTypeConfiguration<PhoneVerificationToken>
{
    public void Configure(EntityTypeBuilder<PhoneVerificationToken> builder)
    {
        builder.ToTable("PhoneVerificationTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.TenantId).IsRequired();
        builder.Property(token => token.UserId).IsRequired();
        builder.Property(token => token.PhoneNumber).HasMaxLength(20).IsRequired();
        builder.Property(token => token.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.CreatedAtUtc).IsRequired();
        builder.Property(token => token.ExpiresAtUtc).IsRequired();

        builder.HasIndex(token => new { token.UserId, token.UsedAtUtc });

        builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
