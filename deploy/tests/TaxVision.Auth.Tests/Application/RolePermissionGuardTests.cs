using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Guardarraíl anti-escalada de permisos. RolePermissionGuard es una función pura (no toca
/// infraestructura), así que se testea directo con instancias de Permission.Seed(...).
/// </summary>
public sealed class RolePermissionGuardTests
{
    private static readonly IReadOnlySet<string> NoModules = new HashSet<string>();

    private static Permission Assignable() => Permission.Seed(Guid.NewGuid(), "custom.assignable", "custom", "desc");

    private static Permission Reserved() =>
        Permission.Seed(Guid.NewGuid(), "billing.manage", "billing", "desc", isAssignableByTenant: false);

    // Código de un permiso que sí pertenece a un módulo (signature. -> signatures).
    private static Permission ModulePermission() =>
        Permission.Seed(Guid.NewGuid(), "signature.request.read", "signature", "desc");

    [Fact]
    public void Empty_request_is_always_valid()
    {
        var result = RolePermissionGuard.Validate([], [], NoModules);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Assignable_permission_without_a_module_is_accepted()
    {
        var permission = Assignable();

        var result = RolePermissionGuard.Validate([permission], [permission.Id], NoModules);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Reserved_permission_is_rejected_even_with_every_module_enabled()
    {
        var permission = Reserved();

        var result = RolePermissionGuard.Validate([permission], [permission.Id], NoModules);

        Assert.True(result.IsFailure);
        Assert.Equal("Role.PermissionNotAssignable", result.Error.Code);
        Assert.Contains("billing.manage", result.Error.Message);
    }

    [Fact]
    public void Mixed_request_reports_only_the_rejected_codes()
    {
        var ok = Assignable();
        var reserved = Reserved();
        var catalog = new[] { ok, reserved };

        var result = RolePermissionGuard.Validate(catalog, [ok.Id, reserved.Id], NoModules);

        Assert.True(result.IsFailure);
        Assert.Contains(reserved.Code, result.Error.Message);
        Assert.DoesNotContain(ok.Code, result.Error.Message);
    }

    [Fact]
    public void Unknown_permission_id_is_silently_skipped_here()
    {
        // La existencia la valida por separado CreateRoleHandler.ValidatePermissionIdsAsync
        // (Permission.NotFound) — el guardarraíl no debe duplicar ese error.
        var result = RolePermissionGuard.Validate([], [Guid.NewGuid()], NoModules);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Permission_of_a_module_not_in_the_plan_is_rejected()
    {
        var permission = ModulePermission();

        var result = RolePermissionGuard.Validate([permission], [permission.Id], new HashSet<string> { "documents" });

        Assert.True(result.IsFailure);
        Assert.Contains("signature.request.read", result.Error.Message);
    }

    [Fact]
    public void Permission_of_an_enabled_module_is_accepted()
    {
        var permission = ModulePermission();

        var result = RolePermissionGuard.Validate([permission], [permission.Id], new HashSet<string> { "signatures" });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Empty_module_set_fails_closed_for_a_module_permission()
    {
        var permission = ModulePermission();

        // Sin datos de módulos todavía no se sabe qué contrató la oficina: falla cerrado (decisión F7).
        var result = RolePermissionGuard.Validate([permission], [permission.Id], NoModules);

        Assert.True(result.IsFailure);
        Assert.Contains("signature.request.read", result.Error.Message);
    }
}
