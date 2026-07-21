using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class UserPermissionsProjectionConfiguration : IEntityTypeConfiguration<UserPermissionsProjection>
{
    public void Configure(EntityTypeBuilder<UserPermissionsProjection> builder)
    {
        builder.ToTable("UserPermissionsProjections");
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
        // La consulta de ByPermission escanea todos los activos del tenant y filtra en
        // memoria (PermissionCodesJson no es indexable como columna nvarchar) — este
        // índice acota el escaneo al tenant, igual que el resto de las tablas de esta BD.
        builder.HasIndex(p => new { p.TenantId, p.IsActive });
    }
}
