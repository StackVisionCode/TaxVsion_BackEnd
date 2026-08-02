using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Subscriptions.Commands;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Internal;

/// <summary>PayFlow (Fase 16) — M2M-only: la Saga de onboarding de Auth (Fase 15) invoca este
/// endpoint para activar la suscripción de un tenant recién provisionado, directo en Active.</summary>
[ApiController]
[Route("subscriptions/internal")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalSubscriptionActivationController(IMessageBus bus) : ControllerBase
{
    public sealed record ActivateFromOnboardingRequest(Guid OnboardingId, Guid TenantId, Guid PlanId);

    /// <summary>Fase 4.10 (rate limiting) — M2M-only ([AllowActorTypes(ActorType.Service)],
    /// nunca expuesto al Gateway), mismo patrón que Postmaster Fase 4.4/Connectors Fase 4.8.</summary>
    [HttpPost("activate-from-onboarding")]
    [RateLimitExempt("M2M-only endpoint invoked by Auth's onboarding Saga — never exposed to the Gateway.")]
    public async Task<IActionResult> ActivateFromOnboarding(
        [FromBody] ActivateFromOnboardingRequest request,
        CancellationToken ct
    )
    {
        var command = new ActivateFromOnboardingCommand(request.OnboardingId, request.TenantId, request.PlanId);

        var result = await bus.InvokeAsync<Result>(command, ct);
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
