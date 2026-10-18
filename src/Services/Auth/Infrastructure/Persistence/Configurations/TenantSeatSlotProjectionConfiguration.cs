using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Infrastructure.Persistence.Projections;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

public sealed class TenantSeatSlotProjectionConfiguration : IEntityTypeConfiguration<TenantSeatSlotProjection>
{
    public void Configure(EntityTypeBuilder<TenantSeatSlotProjection> builder)
    {
        builder.ToTable("TenantSeatSlotProjections");
        builder.HasKey(row => new { row.TenantId, row.UserId });
        builder.Property(row => row.SlotIndex).IsRequired();
        builder.Property(row => row.EffectiveCap).IsRequired();
        builder.Property(row => row.IsGated).IsRequired();
        builder.Property(row => row.ComputedAtUtc).IsRequired();
        builder.Property(row => row.RevisionNumber).IsRequired();
        builder.HasIndex(row => new { row.TenantId, row.SlotIndex }).IsUnique();
        builder.HasIndex(row => new { row.TenantId, row.IsGated });
    }
}
