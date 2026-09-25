using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Infrastructure.Persistence.Configurations;

public sealed class OffboardedStaffProjectionConfiguration : IEntityTypeConfiguration<OffboardedStaffProjection>
{
    public void Configure(EntityTypeBuilder<OffboardedStaffProjection> builder)
    {
        builder.ToTable("OffboardedStaff");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.OffboardedAtUtc).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();

        // Único por (tenant, user) — sirve también al lookup del hot path de autorización.
        builder.HasIndex(p => new { p.TenantId, p.UserId }).IsUnique();
    }
}
