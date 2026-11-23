using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Sessions.Policy;
using TaxVision.Auth.Domain.Roles;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Política de sesión de la plataforma (inactividad, aviso y máximo), solo PlatformAdmin. Reutiliza el permiso de
/// operación de plataforma <see cref="PermissionCatalog.OnboardingAdminManage"/> (PlatformOnly), igual que
/// <c>PermissionsAdminController</c>: evita un permiso nuevo en el catálogo sin perder el gate de plataforma.
/// </summary>
[ApiController]
[Route("auth/admin/session-policy")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(PermissionCatalog.OnboardingAdminManage)]
public sealed class SessionPolicyAdminController(IMessageBus bus) : ControllerBase
{
    public sealed record UpdateSessionPolicyRequest(
        int IdleTimeoutMinutes,
        int WarningSeconds,
        int AbsoluteLifetimeHours,
        string Reason
    );

    [HttpGet]
    [RateLimit("auth.g.onboarding_admin_manage")]
    [ProducesResponseType<SessionPolicyView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SessionPolicyView>>(new GetSessionPolicyQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut]
    [RateLimit("auth.g.onboarding_admin_manage")]
    [ProducesResponseType<SessionPolicyView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(UpdateSessionPolicyRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SessionPolicyView>>(
            new UpdateSessionPolicyCommand(
                request.IdleTimeoutMinutes,
                request.WarningSeconds,
                request.AbsoluteLifetimeHours,
                request.Reason,
                actorId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

/// <summary>Lo que el CRM y el portal necesitan para el aviso de inactividad: minutos, segundos de aviso y máximo.</summary>
[ApiController]
[Route("auth/session-policy")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.CustomerPortal, ActorType.PlatformAdmin)]
public sealed class SessionPolicyController(IMessageBus bus) : ControllerBase
{
    /// <summary>También desde el Account del landing (superficie `account`): muestra el mismo aviso de inactividad.</summary>
    [HttpGet]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("auth.f.session_read")]
    [ProducesResponseType<SessionPolicyView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SessionPolicyView>>(new GetSessionPolicyQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
