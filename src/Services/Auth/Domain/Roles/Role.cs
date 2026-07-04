using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Roles;

/// <summary>Rol por tenant. Los roles de sistema se siembran al crear el tenant y no son editables.</summary>
public sealed class Role : TenantEntity
{
    public const string SystemTenantAdmin = "Tenant Admin";
    public const string SystemEmployee = "Employee";
    public const string SystemCustomerPortal = "Customer Portal";

    private readonly List<RolePermission> _permissions = [];

    private Role() { }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public static Result<Role> Create(Guid tenantId, string name, string? description, bool isSystem = false)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Role>(new Error("Role.Tenant", "Tenant is required."));

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 2 or > 60)
            return Result.Failure<Role>(new Error("Role.Name", "Role name must be 2-60 characters."));

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            Description = description?.Trim(),
            IsSystem = isSystem,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
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
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (IsSystem)
            return Result.Failure(new Error("Role.System", "System roles cannot be deactivated."));

        IsActive = false;
        return Result.Success();
    }
}
