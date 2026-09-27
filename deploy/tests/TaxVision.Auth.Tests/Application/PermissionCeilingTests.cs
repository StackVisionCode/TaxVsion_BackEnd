using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// A4 (§27 del plan) — la matriz del techo de delegación: asignable × tier × módulo × actor, y la
/// separación entre la mitad dura (nunca concedible, se exige en todos los caminos) y la comercial
/// (tier y módulo, solo al escribir configuración nueva de un rol).
/// </summary>
public sealed class PermissionCeilingTests
{
    private static readonly IReadOnlySet<string> NoModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static Permission Operational(
        string code = "customers.view",
        string module = "customers",
        int minPlanTier = 0,
        bool isAssignableByTenant = true,
        bool platformOnly = false,
        bool isDangerous = false,
        bool isReserved = false,
        bool isCustomerPortal = false
    ) =>
        Permission.Seed(
            Guid.NewGuid(),
            code,
            module,
            "desc",
            isCustomerPortal: isCustomerPortal,
            minPlanTier: minPlanTier,
            isAssignableByTenant: isAssignableByTenant,
            platformOnly: platformOnly,
            isDangerous: isDangerous,
            isReserved: isReserved
        );

    [Fact]
    public void An_operational_permission_is_grantable()
    {
        Assert.False(PermissionCeiling.IsNeverGrantable(Operational()));
    }

    [Theory]
    [InlineData(false, false, false, false)] // reservado a la plataforma por IsAssignableByTenant
    [InlineData(true, true, false, false)] // PlatformOnly explícito
    [InlineData(true, false, true, false)] // peligroso explícito
    [InlineData(true, false, false, true)] // declarado sin nada que proteger
    public void A_permission_outside_the_hard_ceiling_is_never_grantable(
        bool isAssignableByTenant,
        bool platformOnly,
        bool isDangerous,
        bool isReserved
    )
    {
        var permission = Operational(
            isAssignableByTenant: isAssignableByTenant,
            platformOnly: platformOnly,
            isDangerous: isDangerous,
            isReserved: isReserved
        );

        Assert.True(PermissionCeiling.IsNeverGrantable(permission));

        var result = PermissionCeiling.ValidateNeverGrantable([permission], [permission.Id]);
        Assert.True(result.IsFailure);
        Assert.Equal("Role.PermissionNotAssignable", result.Error.Code);
        Assert.Contains(permission.Code, result.Error.Message);
    }

    /// <summary>
    /// El caso que motiva la mitad dura: PlatformOnly e IsDangerous hoy vienen siempre con
    /// IsAssignableByTenant en false, así que el techo viejo los frenaba de rebote. Si alguien
    /// siembra uno peligroso olvidándose de la otra bandera, el techo tiene que seguir frenándolo.
    /// </summary>
    [Fact]
    public void A_dangerous_permission_that_forgot_to_set_the_other_flag_is_still_rejected()
    {
        var permission = Operational(code: "billing.manage", isAssignableByTenant: true, isDangerous: true);

        Assert.True(PermissionCeiling.IsNeverGrantable(permission));
    }

    [Fact]
    public void The_plan_tier_ceiling_rejects_a_permission_above_the_contracted_tier()
    {
        var proPermission = Operational(code: "campaigns.view", module: "campaigns", minPlanTier: (int)PlanTier.Pro);

        Assert.False(PermissionCeiling.IsWithinPlan(proPermission, PlanTier.Starter, NoModules));
        Assert.True(PermissionCeiling.IsWithinPlan(proPermission, PlanTier.Pro, NoModules));
    }

    [Fact]
    public void An_unknown_set_of_modules_does_not_block_anything()
    {
        // Módulos vacíos = todavía no sabemos qué contrató el tenant. El enforcement real es el gate
        // en runtime; bloquear acá dejaría a un tenant sin poder crear roles por falta de datos.
        Assert.True(PermissionCeiling.IsWithinPlan(Operational(), PlanTier.Starter, NoModules));
    }

    [Fact]
    public void A_permission_of_a_module_the_plan_does_not_enable_is_not_within_the_plan()
    {
        var campaigns = Operational(code: "campaigns.view", module: "campaigns");
        IReadOnlySet<string> onlyCustomers = new HashSet<string>(["customers"], StringComparer.OrdinalIgnoreCase);

        Assert.False(PermissionCeiling.IsWithinPlan(campaigns, PlanTier.Enterprise, onlyCustomers));
    }

    [Fact]
    public void Grantable_takes_the_target_actor_type_into_account()
    {
        var portal = Operational(code: "portal.folders.view", module: "portal", isCustomerPortal: true);

        Assert.True(
            PermissionCeiling.IsGrantable(portal, PlanTier.Starter, NoModules, UserActorType.CustomerPortal),
            "un permiso de portal es concedible a un rol de portal"
        );
        Assert.False(
            PermissionCeiling.IsGrantable(portal, PlanTier.Starter, NoModules, UserActorType.TenantEmployee),
            "y no a un rol de staff"
        );
        Assert.True(
            PermissionCeiling.IsGrantable(portal, PlanTier.Starter, NoModules, targetActorType: null),
            "sin destino declarado la dimensión de actor no se aplica"
        );
    }

    /// <summary>
    /// La mitad comercial NO corre al asignar, invitar ni aceptar: una configuración anterior a un
    /// downgrade queda dormida (el gate en runtime la vuelve inefectiva), no borrada. Si se midiera
    /// acá, un downgrade dejaría el rol entero inasignable.
    /// </summary>
    [Fact]
    public void The_hard_ceiling_ignores_the_plan_so_a_dormant_permission_does_not_block_an_assignment()
    {
        var proPermission = Operational(code: "campaigns.view", module: "campaigns", minPlanTier: (int)PlanTier.Pro);

        Assert.True(PermissionCeiling.ValidateNeverGrantable([proPermission], [proPermission.Id]).IsSuccess);
        Assert.True(
            PermissionCeiling.Validate([proPermission], [proPermission.Id], PlanTier.Starter, NoModules).IsFailure,
            "pero al escribir configuración nueva del rol sí se mide contra el plan"
        );
    }

    [Fact]
    public void A_system_role_is_never_measured_against_the_tenant_ceiling()
    {
        // El bundle raíz de Tenant Admin incluye permisos IsDangerous por diseño: medirlo contra el
        // techo del tenant dejaría el rol "Tenant Admin" inasignable.
        var dangerous = Operational(code: "roles.manage", isAssignableByTenant: false, isDangerous: true);
        var tenantId = Guid.NewGuid();

        var systemRole = Role.Create(tenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;
        systemRole.SetPermissions([dangerous.Id], seeding: true);

        var customRole = Role.Create(tenantId, "Rol custom", null).Value;
        customRole.SetPermissions([dangerous.Id]);

        Assert.True(PermissionCeiling.ValidateRolesNeverGrantable([systemRole], [dangerous]).IsSuccess);
        Assert.True(PermissionCeiling.ValidateRolesNeverGrantable([customRole], [dangerous]).IsFailure);
    }

    [Fact]
    public void An_unknown_permission_id_is_left_to_the_existence_check()
    {
        var result = PermissionCeiling.Validate([Operational()], [Guid.NewGuid()], PlanTier.Starter, NoModules);

        Assert.True(result.IsSuccess);
    }
}
