using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Guardarraíl anti-escalada de permisos. RolePermissionGuard es una función pura (no toca
/// infraestructura), así que se testea directo con instancias de Permission.Seed(...).
/// </summary>
public sealed class RolePermissionGuardTests
{
    private static readonly IReadOnlySet<string> NoModules = new HashSet<string>();

    private static Permission Assignable(int minPlanTier = 0) =>
        Permission.Seed(Guid.NewGuid(), "custom.assignable", "custom", "desc", minPlanTier: minPlanTier);

    private static Permission Reserved() =>
        Permission.Seed(Guid.NewGuid(), "billing.manage", "billing", "desc", isAssignableByTenant: false);

    // Código de un permiso que sí pertenece a un módulo (signature. -> signatures).
    private static Permission ModulePermission() =>
        Permission.Seed(Guid.NewGuid(), "signature.request.read", "signature", "desc");

    [Fact]
    public void Empty_request_is_always_valid()
    {
        var result = RolePermissionGuard.Validate([], [], PlanTier.Starter, NoModules);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Assignable_permission_within_plan_tier_is_accepted()
    {
        var permission = Assignable(minPlanTier: (int)PlanTier.Starter);

        var result = RolePermissionGuard.Validate([permission], [permission.Id], PlanTier.Starter, NoModules);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Reserved_permission_is_rejected_even_for_a_top_tier_tenant()
    {
        var permission = Reserved();

        var result = RolePermissionGuard.Validate([permission], [permission.Id], PlanTier.Enterprise, NoModules);

        Assert.True(result.IsFailure);
        Assert.Equal("Role.PermissionNotAssignable", result.Error.Code);
        Assert.Contains("billing.manage", result.Error.Message);
    }

    [Fact]
    public void Permission_above_tenant_plan_tier_is_rejected()
    {
        var permission = Assignable(minPlanTier: (int)PlanTier.Pro);

        var result = RolePermissionGuard.Validate([permission], [permission.Id], PlanTier.Starter, NoModules);

        Assert.True(result.IsFailure);
        Assert.Equal("Role.PermissionNotAssignable", result.Error.Code);
    }

    [Fact]
    public void Permission_at_or_above_required_tier_is_accepted()
    {
        var permission = Assignable(minPlanTier: (int)PlanTier.Pro);

        var result = RolePermissionGuard.Validate([permission], [permission.Id], PlanTier.Enterprise, NoModules);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Mixed_request_reports_only_the_rejected_codes()
    {
        var ok = Assignable(minPlanTier: (int)PlanTier.Starter);
        var reserved = Reserved();
        var catalog = new[] { ok, reserved };

        var result = RolePermissionGuard.Validate(catalog, [ok.Id, reserved.Id], PlanTier.Enterprise, NoModules);

        Assert.True(result.IsFailure);
        Assert.Contains(reserved.Code, result.Error.Message);
        Assert.DoesNotContain(ok.Code, result.Error.Message);
    }

    [Fact]
    public void Unknown_permission_id_is_silently_skipped_here()
    {
        // La existencia la valida por separado CreateRoleHandler.ValidatePermissionIdsAsync
        // (Permission.NotFound) — el guardarraíl no debe duplicar ese error.
        var result = RolePermissionGuard.Validate([], [Guid.NewGuid()], PlanTier.Starter, NoModules);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Permission_of_a_module_not_in_the_plan_is_rejected()
    {
        var permission = ModulePermission();

        var result = RolePermissionGuard.Validate(
            [permission],
            [permission.Id],
            PlanTier.Enterprise,
            new HashSet<string> { "documents" }
        );

        Assert.True(result.IsFailure);
        Assert.Contains("signature.request.read", result.Error.Message);
    }

    [Fact]
    public void Permission_of_an_enabled_module_is_accepted()
    {
        var permission = ModulePermission();

        var result = RolePermissionGuard.Validate(
            [permission],
            [permission.Id],
            PlanTier.Enterprise,
            new HashSet<string> { "signatures" }
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Empty_module_set_skips_the_module_ceiling()
    {
        var permission = ModulePermission();

        // Sin datos de módulos (set vacío) no se aplica el ceiling — el gate en runtime es el enforcement real.
        var result = RolePermissionGuard.Validate([permission], [permission.Id], PlanTier.Enterprise, NoModules);

        Assert.True(result.IsSuccess);
    }
}
