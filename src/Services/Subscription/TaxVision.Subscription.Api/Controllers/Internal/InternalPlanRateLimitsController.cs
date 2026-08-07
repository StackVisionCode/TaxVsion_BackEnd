using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.RateLimiting.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Internal;

/// <summary>RateLimit Fase 6 — M2M-only: cada servicio que active tier-aware quotas
/// (piloto: Customer) consulta este catálogo completo y lo cachea localmente (ver
/// HttpPlanRateLimitReader en TaxVision.Customer.Infrastructure). Mismo criterio que
/// <see cref="InternalPlanPricingController"/>.</summary>
[ApiController]
[Route("internal/plan-rate-limits")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalPlanRateLimitsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimitExempt(
        "M2M-only endpoint consultado por otros servicios para cachear el catálogo de multiplicadores por plan — nunca expuesto al Gateway."
    )]
    [ProducesResponseType<IReadOnlyList<PlanRateLimitResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<PlanRateLimitResponse>>>(
            new GetPlanRateLimitsQuery(),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
