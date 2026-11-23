using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

/// <summary>Una sola fila (<c>Id = "global"</c>) con la política de sesión de la plataforma.</summary>
public sealed class PlatformSessionPolicyConfiguration : IEntityTypeConfiguration<PlatformSessionPolicy>
{
    public void Configure(EntityTypeBuilder<PlatformSessionPolicy> builder)
    {
        builder.ToTable("PlatformSessionPolicies");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id).HasMaxLength(16).ValueGeneratedNever();
        builder.Property(policy => policy.IdleTimeoutMinutes).IsRequired();
        builder.Property(policy => policy.WarningSeconds).IsRequired();
        builder.Property(policy => policy.AbsoluteLifetimeHours).IsRequired();
        builder.Property(policy => policy.UpdatedAtUtc).IsRequired();
        builder.Property(policy => policy.UpdatedBy).IsRequired();
        builder.Property(policy => policy.Version).IsRowVersion();
    }
}
