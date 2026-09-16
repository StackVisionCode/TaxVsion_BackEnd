using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notes.Domain.RateLimiting;

namespace TaxVision.Notes.Infrastructure.Persistence.Configurations;

public sealed class TenantPlanCodeProjectionConfiguration : IEntityTypeConfiguration<TenantPlanCodeProjection>
{
    public void Configure(EntityTypeBuilder<TenantPlanCodeProjection> builder)
    {
        builder.ToTable("TenantPlanCodeProjections");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.PlanCode).HasMaxLength(100).IsRequired();
        builder.Property(p => p.RevisionNumber).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();
        // Gate de módulo Fase 1: default "[]" backfilea filas preexistentes con una lista JSON vacía.
        builder.Property(p => p.EnabledModulesJson).HasColumnType("nvarchar(max)").IsRequired().HasDefaultValue("[]");
        builder.Ignore(p => p.EnabledModules); // computada: se deriva de EnabledModulesJson

        builder.HasIndex(p => p.TenantId).IsUnique();
    }
}
