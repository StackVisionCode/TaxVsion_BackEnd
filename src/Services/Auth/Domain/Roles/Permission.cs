using BuildingBlocks.Domain;
using TaxVision.Auth.Domain.Users;

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

    /// <summary>
    /// Si es <c>true</c>, el rol de sistema "Tenant Admin" nunca lo incluye por defecto pese a
    /// tener un caso de uso legítimo para un tenant (a diferencia de <see cref="PlatformOnly"/>,
    /// que excluye permisos sin ningún caso de uso tenant-propio). RBAC Fase 2 (ver
    /// <see cref="TaxVision.Auth.Domain.Roles.PermissionCatalog.SystemRoleDefaults"/>): reserva
    /// permisos de riesgo alto (auto-escalada, financiero, legal, lock-out) a asignación
    /// explícita en vez del bundle automático.
    /// </summary>
    public bool IsDangerous { get; private set; }

    /// <summary>
    /// Qué <see cref="UserActorType"/>(s) pueden llegar a tener este permiso a través de un rol
    /// (Fase 2 de Actor_Type_Authorization_Layers_Plan.md). Siempre concreto en la fila (nunca
    /// null en la base) — si el catálogo no lo especifica explícito, se infiere una única vez al
    /// sembrar a partir de <see cref="PlatformOnly"/>/<see cref="IsCustomerPortal"/>
    /// (ver <see cref="InferAllowedActorTypes"/>), el mismo criterio que ya usa
    /// <see cref="TaxVision.Auth.Domain.Roles.PermissionCatalog.SystemRoleDefaults"/> para excluir
    /// PlatformOnly del bundle de Tenant Admin.
    /// </summary>
    public IReadOnlyList<UserActorType> AllowedActorTypes { get; private set; } = [];

    public static Permission Seed(
        Guid id,
        string code,
        string module,
        string description,
        bool isCustomerPortal = false,
        int minPlanTier = 0,
        bool isAssignableByTenant = true,
        bool platformOnly = false,
        UserActorType[]? allowedActorTypes = null,
        bool isDangerous = false
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
            AllowedActorTypes = allowedActorTypes ?? InferAllowedActorTypes(isCustomerPortal, platformOnly),
            IsDangerous = isDangerous,
        };

    /// <summary>
    /// Regla de default para permisos que no declaran <see cref="AllowedActorTypes"/> explícito
    /// (evita re-anotar los ~140 permisos ya sembrados en una sola migración de riesgo alto —
    /// se puede ir siendo explícito permiso por permiso, Fase 7 del plan). Deliberadamente se usa
    /// <see cref="UserActorType"/> (sin variante "Service") y no el <c>ActorType</c> compartido de
    /// BuildingBlocks: ningún permiso llega a un caller M2M a través de un rol, siempre vía
    /// ServiceAuth:Clients (config) — ver el comentario junto a ScribeRender en PermissionCatalog.
    /// </summary>
    public static UserActorType[] InferAllowedActorTypes(bool isCustomerPortal, bool platformOnly) =>
        platformOnly ? [UserActorType.PlatformAdmin]
        : isCustomerPortal ? [UserActorType.CustomerPortal]
        : [UserActorType.TenantEmployee, UserActorType.TenantAdmin, UserActorType.PlatformAdmin];
}
