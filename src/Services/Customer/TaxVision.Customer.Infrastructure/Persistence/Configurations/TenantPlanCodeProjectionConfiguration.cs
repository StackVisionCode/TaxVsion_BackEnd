using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Customer.Domain.RateLimiting;

namespace TaxVision.Customer.Infrastructure.Persistence.Configurations;

public sealed class TenantPlanCodeProjectionConfiguration : IEntityTypeConfiguration<TenantPlanCodeProjection>
{
    public void Configure(EntityTypeBuilder<TenantPlanCodeProjection> builder)
    {
        builder.ToTable("TenantPlanCodeProjections");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.PlanCode).HasMaxLength(50).IsRequired();
        builder.Property(p => p.RevisionNumber).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();
        // Default "[]" (no ""): backfilea filas preexistentes con un JSON válido de lista vacía, así
        // EnabledModules deserializa sin reventar.
        builder.Property(p => p.EnabledModulesJson).HasColumnType("nvarchar(max)").IsRequired().HasDefaultValue("[]");
        builder.Ignore(p => p.EnabledModules); // computada: se deriva de EnabledModulesJson

        builder.HasIndex(p => p.TenantId).IsUnique();
    }
}
