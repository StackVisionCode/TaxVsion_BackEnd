using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo EF Core de AuthAuditLog: tabla, longitudes de columnas e índices para consultas de auditoría por tenant, usuario y acción.</summary>
public sealed class AuthAuditLogConfiguration : IEntityTypeConfiguration<AuthAuditLog>
{
    public void Configure(EntityTypeBuilder<AuthAuditLog> builder)
    {
        builder.ToTable("AuthAuditLogs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.TenantId).IsRequired();
        builder.Property(log => log.Action).HasMaxLength(64).IsRequired();
        builder.Property(log => log.TargetType).HasMaxLength(32);
        builder.Property(log => log.IpAddress).HasMaxLength(45);
        builder.Property(log => log.UserAgent).HasMaxLength(512);
        builder.Property(log => log.CorrelationId).HasMaxLength(128);
        builder.Property(log => log.OccurredAtUtc).IsRequired();

        builder.HasIndex(log => new { log.TenantId, log.OccurredAtUtc })
            .IsDescending(false, true);
        builder.HasIndex(log => new { log.TenantId, log.UserId, log.OccurredAtUtc });
        builder.HasIndex(log => new { log.TenantId, log.Action });
    }
}

/// <summary>Mapeo EF Core de TenantPlanLimits: tabla y columnas de los límites del plan (la clave coincide con el TenantId).</summary>
public sealed class TenantPlanLimitsConfiguration : IEntityTypeConfiguration<TenantPlanLimits>
{
    public void Configure(EntityTypeBuilder<TenantPlanLimits> builder)
    {
        builder.ToTable("TenantPlanLimits");
        builder.HasKey(limits => limits.Id);
        builder.Property(limits => limits.Id).ValueGeneratedNever(); // Id = TenantId
        builder.Property(limits => limits.PlanCode).HasMaxLength(32).IsRequired();
        builder.Property(limits => limits.MaxUsers).IsRequired();
        builder.Property(limits => limits.MaxPendingInvitations).IsRequired();
        builder.Property(limits => limits.EnabledModulesJson)
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(limits => limits.UpdatedAtUtc).IsRequired();
    }
}
