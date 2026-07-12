using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class UserPermissionsProjectionConfiguration : IEntityTypeConfiguration<UserPermissionsProjection>
{
    public void Configure(EntityTypeBuilder<UserPermissionsProjection> builder)
    {
        builder.ToTable("UserPermissionsProjections");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.PermissionsVersion).IsRequired();
        builder.Property(p => p.RolesCsv).HasMaxLength(UserPermissionsProjection.MaxRolesJoinedLength).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.UserId }).IsUnique();
    }
}
