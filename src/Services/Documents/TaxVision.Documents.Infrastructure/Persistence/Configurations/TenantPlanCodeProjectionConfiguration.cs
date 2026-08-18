using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Documents.Domain.RateLimiting;

namespace TaxVision.Documents.Infrastructure.Persistence.Configurations;

public sealed class TenantPlanCodeProjectionConfiguration : IEntityTypeConfiguration<TenantPlanCodeProjection>
{
    public void Configure(EntityTypeBuilder<TenantPlanCodeProjection> builder)
    {
        builder.ToTable("TenantPlanCodeProjections", DocumentsSchemas.Documents);
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.PlanCode).HasMaxLength(100).IsRequired();
        builder.Property(p => p.RevisionNumber).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => p.TenantId).IsUnique();
    }
}
