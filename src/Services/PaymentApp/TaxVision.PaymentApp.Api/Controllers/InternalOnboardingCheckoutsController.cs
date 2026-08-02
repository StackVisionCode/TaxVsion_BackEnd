using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>PayFlow (Fase 8) — M2M-only: Auth's onboarding Saga (Fase 15) invoca este endpoint
/// para iniciar el pago inicial de un onboarding pago-primero, antes de que el tenant exista.</summary>
[ApiController]
[Route("payments-app/internal/onboarding")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalOnboardingCheckoutsController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateOnboardingCheckoutRequest(
        Guid OnboardingId,
        Guid PlanId,
        string PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        string IdempotencyKey
    );

    [HttpPost("checkout")]
    [RateLimitExempt(
        "M2M ServiceOnly (Fase 8) — invocado por la Saga de onboarding de Auth (Fase 15), nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<OnboardingCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCheckout(CreateOnboardingCheckoutRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<OnboardingCheckoutResponse>>(
            new CreateOnboardingCheckoutCommand(
                request.OnboardingId,
                request.PlanId,
                request.PayerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                request.IdempotencyKey
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
