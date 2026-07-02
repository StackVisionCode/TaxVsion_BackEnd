using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo EF Core de MfaMethod: tabla, tipo como texto, índice único por usuario/tipo y relación en cascada con el usuario.</summary>
public sealed class MfaMethodConfiguration : IEntityTypeConfiguration<MfaMethod>
{
    public void Configure(EntityTypeBuilder<MfaMethod> builder)
    {
        builder.ToTable("MfaMethods");
        builder.HasKey(method => method.Id);
        builder.Property(method => method.TenantId).IsRequired();
        builder.Property(method => method.UserId).IsRequired();
        builder.Property(method => method.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(method => method.SecretCiphertext).HasMaxLength(512);
        builder.Property(method => method.Destination).HasMaxLength(320);
        builder.Property(method => method.CreatedAtUtc).IsRequired();

        builder.HasIndex(method => new { method.UserId, method.Type }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(method => method.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapeo EF Core de MfaChallenge: tabla, índice único por hash del ticket e índice por expiración para su limpieza.</summary>
public sealed class MfaChallengeConfiguration : IEntityTypeConfiguration<MfaChallenge>
{
    public void Configure(EntityTypeBuilder<MfaChallenge> builder)
    {
        builder.ToTable("MfaChallenges");
        builder.HasKey(challenge => challenge.Id);
        builder.Property(challenge => challenge.TenantId).IsRequired();
        builder.Property(challenge => challenge.UserId).IsRequired();
        builder.Property(challenge => challenge.LoginTicketHash)
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(challenge => challenge.OtpHash).HasMaxLength(64);
        builder.Property(challenge => challenge.CreatedAtUtc).IsRequired();
        builder.Property(challenge => challenge.ExpiresAtUtc).IsRequired();

        builder.HasIndex(challenge => challenge.LoginTicketHash).IsUnique();
        builder.HasIndex(challenge => challenge.ExpiresAtUtc);
    }
}

/// <summary>Mapeo EF Core de RecoveryCode: tabla, propiedad calculada IsUsable ignorada y relación en cascada con el usuario.</summary>
public sealed class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> builder)
    {
        builder.ToTable("RecoveryCodes");
        builder.HasKey(code => code.Id);
        builder.Property(code => code.TenantId).IsRequired();
        builder.Property(code => code.UserId).IsRequired();
        builder.Property(code => code.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(code => code.CreatedAtUtc).IsRequired();
        builder.Ignore(code => code.IsUsable);

        builder.HasIndex(code => new { code.UserId, code.UsedAtUtc });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(code => code.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapeo EF Core de TrustedDevice: tabla, índice único por hash del token y relación en cascada con el usuario.</summary>
public sealed class TrustedDeviceConfiguration : IEntityTypeConfiguration<TrustedDevice>
{
    public void Configure(EntityTypeBuilder<TrustedDevice> builder)
    {
        builder.ToTable("TrustedDevices");
        builder.HasKey(device => device.Id);
        builder.Property(device => device.TenantId).IsRequired();
        builder.Property(device => device.UserId).IsRequired();
        builder.Property(device => device.DeviceTokenHash).HasMaxLength(64).IsRequired();
        builder.Property(device => device.UserAgent).HasMaxLength(512);
        builder.Property(device => device.CreatedAtUtc).IsRequired();
        builder.Property(device => device.ExpiresAtUtc).IsRequired();
        builder.Ignore(device => device.IsActive);

        builder.HasIndex(device => device.DeviceTokenHash).IsUnique();
        builder.HasIndex(device => new { device.UserId, device.RevokedAtUtc });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(device => device.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapeo EF Core de TenantMfaPolicy: tabla y columnas de la política de MFA (la clave coincide con el TenantId).</summary>
public sealed class TenantMfaPolicyConfiguration : IEntityTypeConfiguration<TenantMfaPolicy>
{
    public void Configure(EntityTypeBuilder<TenantMfaPolicy> builder)
    {
        builder.ToTable("TenantMfaPolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id).ValueGeneratedNever(); // Id = TenantId
        builder.Property(policy => policy.RequireForAdmins).IsRequired();
        builder.Property(policy => policy.RequireForEmployees).IsRequired();
        builder.Property(policy => policy.RequireForCustomerPortal).IsRequired();
        builder.Property(policy => policy.TrustedDeviceDays).IsRequired();
        builder.Property(policy => policy.UpdatedAtUtc).IsRequired();
    }
}
