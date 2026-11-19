using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Mobile.Domain.Projections;

namespace TaxVision.Mobile.Infrastructure.Persistence.Configurations;

public sealed class UserPermissionsProjectionConfiguration : IEntityTypeConfiguration<UserPermissionsProjection>
{
    public void Configure(EntityTypeBuilder<UserPermissionsProjection> builder)
    {
        builder.ToTable("UserPermissionsProjections");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        builder.Property(x => x.PermissionCodesJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.RoleIdsJson).HasMaxLength(2000);
    }
}
