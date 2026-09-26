using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Domain.Roles;

/// <summary>Rol por tenant. Los roles de sistema se siembran al crear el tenant y no son editables.</summary>
public sealed class Role : TenantEntity
{
    public const string SystemTenantAdmin = "Tenant Admin";
    public const string SystemEmployee = "Employee";
    public const string SystemCustomerPortal = "Customer Portal";

    private static readonly Error ReservedNameError = new(
        "Role.NameReserved",
        "This role name is reserved by the platform."
    );

    private readonly List<RolePermission> _permissions = [];

    private Role() { }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Sube en cada <see cref="SetPermissions"/> — consumidores externos (ver
    /// RolePermissionsChangedIntegrationEvent) la usan para saber que un set de permisos es
    /// más reciente que el anterior. Mismo patrón que User.PermissionsVersion.</summary>
    public int PermissionsVersion { get; private set; }
    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    /// <summary>
    /// Para qué actor type se creó este rol custom. Lo declara el Tenant Admin al crearlo y no se
    /// vuelve a tocar: es lo que permite editar un rol de <c>CustomerPortal</c> sin que el guard de
    /// actor type lo mida contra el staff (G8 del plan — hasta ahora un rol de portal quedaba
    /// inmutable, porque al editarlo se validaba siempre contra TenantEmployee/TenantAdmin).
    /// <para>
    /// <c>null</c> = no declarado: los roles creados antes de esta columna y los de sistema. Ahí se
    /// sigue cayendo al comportamiento anterior (staff), o a los actor types de los titulares del
    /// rol si los tiene.
    /// </para>
    /// </summary>
    public UserActorType? TargetActorType { get; private set; }

    public static Result<Role> Create(
        Guid tenantId,
        string name,
        string? description,
        bool isSystem = false,
        UserActorType? targetActorType = null
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Role>(new Error("Role.Tenant", "Tenant is required."));

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 2 or > 60)
            return Result.Failure<Role>(new Error("Role.Name", "Role name must be 2-60 characters."));

        // Los roles de sistema se siembran con estos mismos nombres, así que la reserva solo
        // aplica a los custom.
        if (!isSystem && ReservedRoleNames.IsReserved(trimmed))
            return Result.Failure<Role>(ReservedNameError);

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            Description = description?.Trim(),
            IsSystem = isSystem,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            TargetActorType = isSystem ? null : targetActorType,
        };
        role.SetTenant(tenantId);
        return Result.Success(role);
    }

    public Result Update(string name, string? description)
    {
        if (IsSystem)
            return Result.Failure(new Error("Role.System", "System roles cannot be modified."));

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 2 or > 60)
            return Result.Failure(new Error("Role.Name", "Role name must be 2-60 characters."));

        // Renombrar es el otro camino a la colisión: acá el rol ya existe y nunca es de sistema
        // (el guard de arriba corta antes).
        if (ReservedRoleNames.IsReserved(trimmed))
            return Result.Failure(ReservedNameError);

        Name = trimmed;
        Description = description?.Trim();
        return Result.Success();
    }

    /// <summary>Reemplaza el conjunto de permisos. Para roles de sistema solo se permite durante el sembrado.</summary>
    public Result SetPermissions(IReadOnlyCollection<Guid> permissionIds, bool seeding = false)
    {
        if (IsSystem && !seeding)
            return Result.Failure(new Error("Role.System", "System roles cannot be modified."));

        _permissions.Clear();
        _permissions.AddRange(permissionIds.Distinct().Select(id => RolePermission.Create(Id, id)));
        PermissionsVersion++;
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (IsSystem)
            return Result.Failure(new Error("Role.System", "System roles cannot be deactivated."));

        IsActive = false;
        return Result.Success();
    }

    /// <summary>
    /// Vuelve a poner en servicio un rol desactivado. Desactivar es reversible a propósito (no se
    /// borra nada), así que reactivar no revalida el techo: los permisos que el rol ya tenía siguen
    /// siendo los mismos y el gate de módulo en runtime es el que decide si alguno quedó dormido.
    /// </summary>
    public Result Reactivate()
    {
        if (IsSystem)
            return Result.Failure(new Error("Role.System", "System roles cannot be reactivated."));

        if (IsActive)
            return Result.Failure(new Error("Role.AlreadyActive", "Role is already active."));

        IsActive = true;
        return Result.Success();
    }
}
