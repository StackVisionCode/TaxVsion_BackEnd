using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Roles;

/// <summary>
/// Permiso atómico del catálogo global. Se siembra por migración a partir de
/// <see cref="PermissionCatalog"/> y no es editable por los tenants.
/// </summary>
public sealed class Permission : BaseEntity
{
    private Permission() { }

    public string Code { get; private set; } = default!;
    public string Module { get; private set; } = default!;
    public string Description { get; private set; } = default!;

    /// <summary>Permisos que aplican al portal del cliente final.</summary>
    public bool IsCustomerPortal { get; private set; }

    public static Permission Seed(
        Guid id,
        string code,
        string module,
        string description,
        bool isCustomerPortal = false) =>
        new()
        {
            Id = id,
            Code = code,
            Module = module,
            Description = description,
            IsCustomerPortal = isCustomerPortal
        };
}
