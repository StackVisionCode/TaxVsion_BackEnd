using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Connectors.Domain.RateLimiting;

namespace TaxVision.Connectors.Infrastructure.Persistence.Configurations;

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
        builder.Property(p => p.EnabledModulesJson).HasColumnType("nvarchar(max)").IsRequired().HasDefaultValue("[]");
        builder.Ignore(p => p.EnabledModules); // computada

        builder.HasIndex(p => p.TenantId).IsUnique();
    }
}
