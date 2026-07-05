using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo EF Core de Permission: tabla, índice único por código y sembrado del catálogo global de permisos.</summary>
public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(permission => permission.Id);
        builder.Property(permission => permission.Code).HasMaxLength(64).IsRequired();
        builder.Property(permission => permission.Module).HasMaxLength(32).IsRequired();
        builder.Property(permission => permission.Description).HasMaxLength(256).IsRequired();
        builder.HasIndex(permission => permission.Code).IsUnique();

        // Sembrado del catálogo global (GUID fijos definidos en PermissionCatalog).
        builder.HasData(
            PermissionCatalog.All.Select(definition => new
            {
                definition.Id,
                definition.Code,
                definition.Module,
                definition.Description,
                definition.IsCustomerPortal,
            })
        );
    }
}

/// <summary>Mapeo EF Core de Role: tabla, índice único por tenant/nombre y relación con sus permisos (acceso por campo).</summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.TenantId).IsRequired();
        builder.Property(role => role.Name).HasMaxLength(60).IsRequired();
        builder.Property(role => role.Description).HasMaxLength(256);
        builder.Property(role => role.IsSystem).IsRequired();
        builder.Property(role => role.IsActive).IsRequired();
        builder.Property(role => role.CreatedAtUtc).IsRequired();

        builder.HasIndex(role => new { role.TenantId, role.Name }).IsUnique();

        builder
            .HasMany(role => role.Permissions)
            .WithOne()
            .HasForeignKey(link => link.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(role => role.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Mapeo EF Core de RolePermission: tabla de enlace rol-permiso con clave compuesta y relación en cascada con el permiso.</summary>
public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(link => new { link.RoleId, link.PermissionId });

        builder
            .HasOne<Permission>()
            .WithMany()
            .HasForeignKey(link => link.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapeo EF Core de UserRole: tabla de enlace usuario-rol con clave compuesta y relaciones en cascada con usuario y rol.</summary>
public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(link => new { link.UserId, link.RoleId });
        builder.Property(link => link.AssignedAtUtc).IsRequired();

        builder.HasIndex(link => link.RoleId);

        builder.HasOne<User>().WithMany().HasForeignKey(link => link.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Role>().WithMany().HasForeignKey(link => link.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}
