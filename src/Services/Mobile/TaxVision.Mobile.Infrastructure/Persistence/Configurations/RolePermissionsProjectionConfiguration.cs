using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Mobile.Domain.Projections;

namespace TaxVision.Mobile.Infrastructure.Persistence.Configurations;

public sealed class RolePermissionsProjectionConfiguration : IEntityTypeConfiguration<RolePermissionsProjection>
{
    public void Configure(EntityTypeBuilder<RolePermissionsProjection> builder)
    {
        builder.ToTable("RolePermissionsProjections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RoleName).HasMaxLength(100);
        builder.Property(x => x.PermissionCodesJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => new { x.TenantId, x.Id }).IsUnique();
    }
}
