using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Plans.Queries;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Internal;

/// <summary>PayFlow (Fase 16) — M2M-only: PaymentApp lo consulta para resolver el precio real de
/// un plan antes de crear un Stripe Checkout Session, en vez de confiar en el precio que envía el
/// frontend. Ver doc-comment de <see cref="GetInternalPlanPricingHandler"/>.</summary>
[ApiController]
[Route("internal/plans")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalPlanPricingController(IMessageBus bus) : ControllerBase
{
    /// <summary>Fase 4.10 (rate limiting) — M2M-only, mismo criterio que
    /// <see cref="InternalSubscriptionActivationController"/>.</summary>
    [HttpGet("{planId:guid}/pricing")]
    [RateLimitExempt(
        "M2M-only endpoint queried by PaymentApp to resolve plan pricing server-side — never exposed to the Gateway."
    )]
    [ProducesResponseType<InternalPlanPricingResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPricing(Guid planId, [FromQuery] string? cycle, CancellationToken ct)
    {
        // ?cycle=Monthly|Yearly|… — ausente/inválido cae a Monthly (compat con callers que aún no lo mandan).
        if (!PlanPricing.TryParseBillingCycle(cycle, out var parsed))
            return StatusCode(
                StatusCodes.Status400BadRequest,
                new Error("Subscription.Plan.InvalidCycle", $"Unknown billing cycle '{cycle}'.")
            );

        var result = await bus.InvokeAsync<Result<InternalPlanPricingResponse>>(
            new GetInternalPlanPricingQuery(planId, parsed ?? BillingCycle.Monthly),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
