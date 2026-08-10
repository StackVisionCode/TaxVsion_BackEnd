using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow (Fase 9) — punto de entrada real del flujo pago-primero: crea el
/// <c>TenantOnboarding</c> (UoW #1) y dispara el checkout en PaymentApp. Ambos endpoints son
/// anónimos por diseño — el comprador todavía no tiene sesión ni tenant.</summary>
[ApiController]
[Route("onboarding")]
public sealed class OnboardingCheckoutController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateOnboardingRequest(
        string Email,
        string FirstName,
        string LastName,
        string? Phone,
        Guid PlanId,
        Guid EmailVerificationChallengeId,
        // Ciclo de facturación elegido ("Monthly"/"Yearly"); ausente = Monthly.
        string? BillingCycle = null
    );

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-checkout-create")]
    [RateLimitExempt(
        "Anónimo (Fase 9) — conserva el limiter nativo onboarding-checkout-create, sin JWT que particionar."
    )]
    [ProducesResponseType<CreateOnboardingResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateOnboardingRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CreateOnboardingResponse>>(
            new CreateOnboardingCommand(
                request.Email,
                request.FirstName,
                request.LastName,
                request.Phone,
                request.PlanId,
                request.EmailVerificationChallengeId,
                request.BillingCycle
            ),
            ct
        );

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    public sealed record StartCheckoutRequest(
        Guid OnboardingId,
        string PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        // Gift/Referral: códigos opcionales. Si un código cubre el 100%, la respuesta trae FullyCovered=true
        // (sin CheckoutUrl) y el comprador recibe el email de registro directamente.
        string? ReferralCode = null,
        string? PromoCode = null,
        string? GiftCode = null
    );

    [HttpPost("checkout")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-checkout-create")]
    [RateLimitExempt(
        "Anónimo (Fase 9) — conserva el limiter nativo onboarding-checkout-create, sin JWT que particionar."
    )]
    [ProducesResponseType<StartOnboardingCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Checkout(StartCheckoutRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<StartOnboardingCheckoutResponse>>(
            new StartOnboardingCheckoutCommand(
                request.OnboardingId,
                request.PayerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                request.ReferralCode,
                request.PromoCode,
                request.GiftCode
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CancelOnboardingRequest(string? Reason = null);

    /// <summary>Cancela explícitamente el onboarding (checkout cancelado por el comprador) y libera al
    /// instante las reservas de código en Growth, sin esperar el vencimiento de 24h. Anónimo, idempotente.</summary>
    [HttpPost("{onboardingId:guid}/cancel")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-checkout-create")]
    [RateLimitExempt(
        "Anónimo (Fase 9) — conserva el limiter nativo onboarding-checkout-create, sin JWT que particionar."
    )]
    public async Task<IActionResult> Cancel(
        Guid onboardingId,
        CancelOnboardingRequest? request,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result>(
            new CancelOnboardingCommand(onboardingId, request?.Reason),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
