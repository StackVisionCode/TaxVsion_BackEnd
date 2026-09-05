using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Permissions.Admin.Commands;
using TaxVision.Auth.Domain.Roles;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>Break-glass de PlatformAdmin para el subsistema de autorización RBAC async.
/// PlatformAdmin-only: opera sobre usuarios de CUALQUIER tenant (el propio handler sella el tenant
/// del usuario objetivo), así que "cross-tenant" no aplica acá.
/// <para>
/// Reutiliza el permiso <see cref="PermissionCatalog.OnboardingAdminManage"/> (PlatformOnly) a
/// propósito: es el permiso de operación de plataforma "break-glass" que ya tienen los operadores,
/// y el caso de uso principal —re-proyectar un owner cuya proyección quedó a medias tras un
/// aprovisionamiento— es exactamente una recuperación de onboarding. Evita agregar un permiso nuevo
/// al catálogo (que exigiría migración HasData en Auth) sin perder el gate PlatformOnly.
/// </para></summary>
[ApiController]
[Route("auth/permissions/admin")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(PermissionCatalog.OnboardingAdminManage)]
public sealed class PermissionsAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>Re-publica <c>UserRolesChangedIntegrationEvent</c> para el usuario, con su set de
    /// roles/permisos actual, de modo que cada microservicio re-materialice su proyección local de
    /// permisos. Recuperación para cuando la proyección quedó ausente/desincronizada en algún
    /// servicio sin necesitar reiniciar Auth. No bumpea la versión (no invalida JWT vigentes).</summary>
    [HttpPost("users/{userId:guid}/reproject")]
    [RateLimit("auth.g.onboarding_admin_manage")]
    [ProducesResponseType<ReprojectUserPermissionsResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReprojectUser(Guid userId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ReprojectUserPermissionsResult>>(
            new ReprojectUserPermissionsCommand(userId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
