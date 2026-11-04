using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Compliance.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Pre-flight del retiro de un empleado (mismo patrón que <c>GET /customers/offboarding-impact/{userId}</c>):
/// lo que en Tenant cambia al retirarlo. Solo dentro del flujo de gestión de usuarios (<c>users.manage</c>).
/// </summary>
[ApiController]
[Route("tenants/offboarding-impact")]
[Authorize]
public sealed class TenantOffboardingImpactController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{userId:guid}")]
    [HasPermission(UserManagementPermissions.UsersManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("tenant.f.branding_read")]
    [ProducesResponseType<TenantOffboardingImpactResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(Guid userId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        return Ok(
            await bus.InvokeAsync<TenantOffboardingImpactResponse>(
                new GetTenantOffboardingImpactQuery(tenantId, userId),
                ct
            )
        );
    }
}
