using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;

namespace TaxVision.Signature.Tests.Authorization;

/// <summary>
/// RBAC Fase 2 (RBAC_Hardening_Plan.md) — regresión del chequeo defensivo inline
/// <c>if (!User.IsPlatformAdmin()) return Forbid();</c> retirado de
/// <see cref="TaxVision.Signature.Api.Controllers.SignatureAdminController.UpdateConstraints"/>. Ese
/// chequeo era redundante (ver comentario en el controller): la fase elimina la raíz real
/// (SignaturePlanConstraintsManage ya nunca llega a un TenantAdmin — ver
/// PermissionCatalogTests.SystemTenantAdmin_does_not_include_dangerous_permissions en Auth.Tests),
/// pero el guardarraíl que sigue viviendo en runtime es <see cref="AllowActorTypesAttribute"/> a
/// nivel de clase. Este test no puede ver el catálogo de Auth (bounded context distinto, sin
/// referencia de proyecto cruzada) — solo confirma que el guardarraíl de Layer 2 sigue ahí.
/// </summary>
public sealed class SignatureAdminAuthorizationRegressionTests
{
    private static readonly Type ControllerType = typeof(TaxVision.Signature.Api.Controllers.SignatureAdminController);

    [Fact]
    public void SignatureAdminController_only_allows_PlatformAdmin_at_class_level()
    {
        var classAllowActorTypes = ControllerType.GetCustomAttribute<AllowActorTypesAttribute>();

        Assert.NotNull(classAllowActorTypes);
        Assert.Equal([ActorType.PlatformAdmin], classAllowActorTypes!.ActorTypes);
    }

    [Fact]
    public void UpdateConstraints_does_not_declare_a_narrower_or_wider_AllowActorTypes()
    {
        var method = ControllerType.GetMethod(
            nameof(TaxVision.Signature.Api.Controllers.SignatureAdminController.UpdateConstraints)
        )!;

        // Sin override a nivel de método: el guardarraíl de clase (PlatformAdmin-only) debe
        // seguir aplicando sin condiciones a esta acción.
        Assert.Null(method.GetCustomAttribute<AllowActorTypesAttribute>());
    }
}
