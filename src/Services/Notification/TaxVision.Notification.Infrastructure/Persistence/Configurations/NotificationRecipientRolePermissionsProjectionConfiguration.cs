using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class NotificationRecipientRolePermissionsProjectionConfiguration
    : IEntityTypeConfiguration<NotificationRecipientRolePermissionsProjection>
{
    public void Configure(EntityTypeBuilder<NotificationRecipientRolePermissionsProjection> builder)
    {
        builder.ToTable("NotificationRecipientRolePermissionsProjections");
        // Id es el propio RoleId de Auth (clave natural) — ver comentario de la entidad.
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.RoleName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.PermissionCodesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(p => p.PermissionsVersion).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => p.TenantId);
    }
}
