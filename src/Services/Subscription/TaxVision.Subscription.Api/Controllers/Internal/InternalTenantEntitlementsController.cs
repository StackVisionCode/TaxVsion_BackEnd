using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Entitlements.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Internal;

/// <summary>
/// M2M-only: resumen de entitlements de un tenant para que un servicio repare su proyección local del plan cuando
/// le falta la fila (servicio nuevo o evento perdido). Mismo contenido que <c>GET entitlements/summary</c>.
/// </summary>
[ApiController]
[Route("internal/tenants")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalTenantEntitlementsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{tenantId:guid}/entitlements")]
    [RateLimitExempt(
        "M2M-only endpoint queried by services to repair their local entitlements projection — never exposed to the Gateway."
    )]
    [ProducesResponseType<EntitlementSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(Guid tenantId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<EntitlementSummaryResponse>>(
            new GetTenantEntitlementSummaryQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
