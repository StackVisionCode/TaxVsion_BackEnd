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

    /// <summary>
    /// Nivel de plan mínimo (ver <see cref="TaxVision.Auth.Domain.Tenants.PlanTier"/>) que debe
    /// tener contratado un tenant para que este permiso pueda incluirse en uno de sus roles
    /// (de sistema o custom). 0 = disponible desde el plan más básico.
    /// </summary>
    public int MinPlanTier { get; private set; }

    /// <summary>
    /// Si es <c>false</c>, ningún rol custom creado por un tenant puede incluir este permiso
    /// (solo puede vivir en los roles de sistema sembrados por la plataforma). Guardarraíl
    /// anti-escalada: reserva permisos sensibles (billing, asientos, gestión de roles) al
    /// control exclusivo de la plataforma. Ver <see cref="TaxVision.Auth.Application.Common.RolePermissionGuard"/>.
    /// No confundir con <see cref="PlatformOnly"/>: este flag es sobre DELEGAR el permiso a un
    /// empleado, no sobre si el propio TenantAdmin lo tiene.
    /// </summary>
    public bool IsAssignableByTenant { get; private set; }

    /// <summary>
    /// Si es <c>true</c>, el rol de sistema "Tenant Admin" nunca lo incluye por defecto — sin
    /// caso de uso legítimo para un tenant, exclusivo de PlatformAdmin (ej. techos de plan de
    /// Signature). Distinto de <see cref="IsAssignableByTenant"/>: ese controla si el TenantAdmin
    /// puede delegarlo a un empleado; este controla si el propio TenantAdmin lo tiene.
    /// </summary>
    public bool PlatformOnly { get; private set; }

    public static Permission Seed(
        Guid id,
        string code,
        string module,
        string description,
        bool isCustomerPortal = false,
        int minPlanTier = 0,
        bool isAssignableByTenant = true,
        bool platformOnly = false
    ) =>
        new()
        {
            Id = id,
            Code = code,
            Module = module,
            Description = description,
            IsCustomerPortal = isCustomerPortal,
            MinPlanTier = minPlanTier,
            IsAssignableByTenant = isAssignableByTenant,
            PlatformOnly = platformOnly,
        };
}
