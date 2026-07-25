using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Permissions;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class AuthzRolePermissionsProjectionConfiguration
    : IEntityTypeConfiguration<AuthzRolePermissionsProjection>
{
    public void Configure(EntityTypeBuilder<AuthzRolePermissionsProjection> builder)
    {
        builder.ToTable("AuthzRolePermissionsProjections");
        // Id es el propio RoleId de Auth (clave natural) — ver comentario de la entidad.
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.RoleName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.PermissionCodesJson).HasMaxLength(4000).IsRequired();
        builder.Property(p => p.PermissionsVersion).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => p.TenantId);
    }
}
