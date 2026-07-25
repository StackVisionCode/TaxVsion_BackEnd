using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Entitlements.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("entitlements")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class EntitlementsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<EntitlementSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<EntitlementSummaryResponse>>(
            new GetTenantEntitlementSummaryQuery(tenantId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{key}")]
    [ProducesResponseType<EntitlementValueResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByKey(string key, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<EntitlementValueResponse>>(
            new GetTenantEntitlementByKeyQuery(tenantId, key),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
