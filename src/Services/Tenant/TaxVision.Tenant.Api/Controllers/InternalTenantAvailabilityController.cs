using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Tenants.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>PayFlow (Fase 14) — M2M-only: Auth's TenantSubdomainAvailabilityClient invoca este
/// endpoint durante el registro post-pago, antes de que el tenant exista.</summary>
[ApiController]
[Route("tenants/internal")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalTenantAvailabilityController(IMessageBus bus) : ControllerBase
{
    [HttpGet("subdomain-available")]
    [ProducesResponseType<SubdomainAvailabilityResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckSubdomainAvailable([FromQuery] string slug, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<SubdomainAvailabilityResponse>(
            new CheckInternalSubdomainAvailabilityQuery(slug),
            ct
        );

        return Ok(response);
    }
}
