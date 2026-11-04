using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Application.Requests.Queries.OffboardingImpact;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Pre-flight del retiro de un empleado (mismo patrón y permiso que Customer/Tasks): cuánto de su trabajo
/// en firmas se reasigna, se descarta o necesita a un admin. El CRM lo junta en el diálogo de offboard.
/// </summary>
[ApiController]
[Route("signature")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SignatureOffboardingController(IMessageBus bus) : ControllerBase
{
    // ---------- GET /signature/offboarding-impact/{userId} ----------
    [HttpGet("offboarding-impact/{userId:guid}")]
    [HasPermission(UserManagementPermissions.UsersManage)]
    [RateLimit("signature.f.request_read")]
    [ProducesResponseType<SignatureOffboardingImpactResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OffboardingImpact(Guid userId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var impact = await bus.InvokeAsync<SignatureOffboardingImpactResponse>(
            new SignatureOffboardingImpactQuery(tenantId, userId),
            ct
        );
        return Ok(impact);
    }
}
