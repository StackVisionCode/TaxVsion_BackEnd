using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;
using TaxVision.PaymentApp.Application.OnboardingPaymentOptions;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>PayFlow (Fase 8) — M2M-only: Auth's onboarding Saga (Fase 15) invoca este endpoint
/// para iniciar el pago inicial de un onboarding pago-primero, antes de que el tenant exista.</summary>
[ApiController]
[Route("internal/onboarding")]
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
        string IdempotencyKey,
        PaymentProviderCode? Provider = null,
        PaymentMethodKind? Method = null,
        // Ciclo elegido ("Monthly"/"Yearly"); ausente = Monthly.
        string? BillingCycle = null,
        // Gift/Referral: neto a cobrar (override del bruto) + resumen de la reserva, si un código aplicó.
        long? NetAmountCents = null,
        long? DiscountAmountCents = null,
        string? Currency = null,
        Guid? CodeReservationId = null,
        string? PromotionSnapshotHash = null
    );

    public sealed record ReconcileOnboardingCheckoutRequest(Guid PaymentId);

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
                request.IdempotencyKey,
                request.Provider ?? PaymentProviderCode.Stripe,
                request.Method ?? PaymentMethodKind.Card,
                string.IsNullOrWhiteSpace(request.BillingCycle) ? "Monthly" : request.BillingCycle,
                request.NetAmountCents,
                request.DiscountAmountCents,
                request.Currency,
                request.CodeReservationId,
                request.PromotionSnapshotHash
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("payment-options")]
    [RateLimitExempt(
        "M2M ServiceOnly (Fase 3 catalogo pagos) -- Auth lo proxyfica al frontend post-OTP; PaymentApp conserva ownership del catalogo."
    )]
    [ProducesResponseType<OnboardingPaymentOptionsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentOptions(
        [FromQuery] Guid planId,
        [FromQuery] string? billingCycle,
        [FromQuery] string? currency,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<OnboardingPaymentOptionsResponse>>(
            new GetOnboardingPaymentOptionsQuery(
                planId,
                string.IsNullOrWhiteSpace(billingCycle) ? "Monthly" : billingCycle,
                currency
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("reconcile-payment")]
    [RateLimitExempt(
        "M2M ServiceOnly (onboarding payment reconcile) -- Auth invokes this after provider redirect; webhook remains the source-of-truth fallback."
    )]
    [ProducesResponseType<ReconcileOnboardingCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReconcilePayment(ReconcileOnboardingCheckoutRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ReconcileOnboardingCheckoutResponse>>(
            new ReconcileOnboardingCheckoutCommand(request.PaymentId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
