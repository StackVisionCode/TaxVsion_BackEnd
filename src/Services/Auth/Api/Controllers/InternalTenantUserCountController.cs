using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Tenants.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>M2M-only: Subscription lo consulta antes de agendar un downgrade — solo Auth sabe cuántos
/// usuarios activos tiene la oficina, y sin ese dato el guard del cupo no se puede aplicar.</summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/user-count")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalTenantUserCountController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimitExempt(
        "M2M ServiceOnly — invocado por Subscription (guard de downgrade); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<TenantUserCountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<TenantUserCountResponse>>(new GetTenantUserCountQuery(tenantId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
