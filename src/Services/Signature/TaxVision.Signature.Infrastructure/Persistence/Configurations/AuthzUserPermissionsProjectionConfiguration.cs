using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Permissions;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class AuthzUserPermissionsProjectionConfiguration
    : IEntityTypeConfiguration<AuthzUserPermissionsProjection>
{
    public void Configure(EntityTypeBuilder<AuthzUserPermissionsProjection> builder)
    {
        // Nombre de tabla distinto de "UserPermissionsProjections" (ya usada por la proyección
        // de auditoría preexistente TaxVision.Signature.Domain.Projections.UserPermissionsProjection)
        // — ver docblock de la entidad.
        builder.ToTable("AuthzUserPermissionsProjections");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.PermissionsVersion).IsRequired();
        builder.Property(p => p.PermissionCodesJson).HasMaxLength(4000).IsRequired();
        builder.Property(p => p.RoleIdsJson).HasMaxLength(2000).IsRequired();
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.UserId }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.IsActive });
    }
}
