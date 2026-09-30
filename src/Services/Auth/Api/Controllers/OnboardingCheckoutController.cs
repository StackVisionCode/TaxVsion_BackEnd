using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.PaymentOptions;
using TaxVision.Auth.Application.Onboarding.Sessions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow entry point: creates the pre-tenant onboarding and starts checkout.</summary>
[ApiController]
[Route("onboarding")]
public sealed class OnboardingCheckoutController(
    IMessageBus bus,
    OnboardingSessionService sessions,
    IOnboardingReturnReferenceStore returnReferences
) : ControllerBase
{
    public sealed record CreateOnboardingRequest(
        string Email,
        string FirstName,
        string LastName,
        string? Phone,
        Guid PlanId,
        Guid EmailVerificationChallengeId,
        string? BillingCycle = null
    );

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-create")]
    [RateLimitExempt("Anonymous onboarding checkout creation keeps the native limiter; no JWT exists yet.")]
    [ProducesResponseType<CreateOnboardingResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateOnboardingRequest request, CancellationToken ct)
    {
        var sessionToken = OnboardingSessionHttp.ReadToken(Request);
        var sessionResult = await ValidateSessionForChallengeAsync(
            sessionToken,
            request.Email,
            request.EmailVerificationChallengeId,
            ct
        );
        if (sessionResult.IsFailure)
            return StatusCode(sessionResult.Error.ToHttpStatusCode(), sessionResult.Error);

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

        await sessions.BindOnboardingAsync(
            sessionToken!,
            sessionResult.Value,
            result.Value.OnboardingId,
            DateTime.UtcNow,
            ct
        );

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    public sealed record StartCheckoutRequest(
        Guid OnboardingId,
        string PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        string? Provider = null,
        string? Method = null,
        string? ReferralCode = null,
        string? PromoCode = null,
        string? GiftCode = null
    );

    [HttpPost("checkout")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-checkout")]
    [RateLimitExempt("Anonymous onboarding checkout start keeps the native limiter; no JWT exists yet.")]
    [ProducesResponseType<StartOnboardingCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Checkout(StartCheckoutRequest request, CancellationToken ct)
    {
        var sessionResult = await ValidateSessionForCheckoutAsync(
            OnboardingSessionHttp.ReadToken(Request),
            request.OnboardingId,
            request.PayerEmail,
            ct
        );
        if (sessionResult.IsFailure)
            return StatusCode(sessionResult.Error.ToHttpStatusCode(), sessionResult.Error);

        var result = await bus.InvokeAsync<Result<StartOnboardingCheckoutResponse>>(
            new StartOnboardingCheckoutCommand(
                request.OnboardingId,
                request.PayerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                string.IsNullOrWhiteSpace(request.Provider) ? "Stripe" : request.Provider,
                string.IsNullOrWhiteSpace(request.Method) ? "Card" : request.Method,
                ReferralCode: request.ReferralCode,
                PromoCode: request.PromoCode,
                GiftCode: request.GiftCode
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ResumeCheckoutRequest(
        string Reference,
        string SuccessUrl,
        string CancelUrl,
        string? Provider = null,
        string? Method = null
    );

    /// <summary>Reanuda el pago de un onboarding fallido desde el link del email de "pago fallido". La
    /// referencia opaca (hash→onboardingId en Redis, TTL = ventana de reintento) ES la autorización: no
    /// exige la cookie de sesión, así el comprador puede abrir el link en otro navegador o dispositivo.
    /// No es credencial de registro; solo permite recobrar el pago del mismo onboarding (sin doble cobro,
    /// reusando el SaaSPayment del lado de PaymentApp), respetando el tope+ventana de reintento.</summary>
    [HttpPost("resume-checkout")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-checkout")]
    [RateLimitExempt(
        "Anonymous onboarding checkout resume keeps the native limiter; the opaque reference is the authorization."
    )]
    [ProducesResponseType<StartOnboardingCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResumeCheckout(ResumeCheckoutRequest request, CancellationToken ct)
    {
        if (await returnReferences.ResolveAsync(request.Reference, ct) is not { } onboardingId)
            return StatusCode(
                StatusCodes.Status410Gone,
                new Error("Onboarding.ResumeReferenceExpired", "The retry link has expired. Please start again.")
            );

        var result = await bus.InvokeAsync<Result<StartOnboardingCheckoutResponse>>(
            new StartOnboardingCheckoutCommand(
                onboardingId,
                PayerEmail: string.Empty,
                request.SuccessUrl,
                request.CancelUrl,
                string.IsNullOrWhiteSpace(request.Provider) ? "Stripe" : request.Provider,
                string.IsNullOrWhiteSpace(request.Method) ? "Card" : request.Method
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CancelOnboardingRequest(string? Reason = null);

    public sealed record ReconcilePaymentRequest(string? Reference = null);

    [HttpGet("payment-options")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-status")]
    [RateLimitExempt("Anonymous onboarding payment-options keeps the native limiter; a post-OTP session is required.")]
    [ProducesResponseType<OnboardingPaymentOptionsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PaymentOptions(
        [FromQuery] Guid planId,
        [FromQuery] string? billingCycle,
        [FromQuery] string? currency,
        CancellationToken ct
    )
    {
        var sessionResult = await sessions.ValidateAsync(OnboardingSessionHttp.ReadToken(Request), DateTime.UtcNow, ct);
        if (sessionResult.IsFailure)
            return StatusCode(sessionResult.Error.ToHttpStatusCode(), sessionResult.Error);

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
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-payment-poll")]
    [RateLimitExempt(
        "Anonymous onboarding payment reconcile keeps the native limiter; a bound post-OTP session is required."
    )]
    [ProducesResponseType<ReconcileOnboardingPaymentResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReconcilePayment(ReconcilePaymentRequest? request, CancellationToken ct)
    {
        var sessionResult = await sessions.ValidateAsync(OnboardingSessionHttp.ReadToken(Request), DateTime.UtcNow, ct);

        // Camino preferido: cookie de sesión (segura, no viaja en la URL). Devuelve estado + registrationUrl.
        if (sessionResult.IsSuccess && sessionResult.Value.OnboardingId is { } cookieOnboardingId)
            return await ReconcileAsync(cookieOnboardingId, includeRegistrationUrl: true, ct);

        // Fallback: referencia de retorno del successUrl (otro navegador/incógnito/cookies bloqueadas).
        // Solo estado — nunca la registrationUrl (que sigue llegando por email).
        if (!string.IsNullOrWhiteSpace(request?.Reference))
        {
            var referencedOnboardingId = await returnReferences.ResolveAsync(request.Reference, ct);
            if (referencedOnboardingId is { } onboardingId)
                return await ReconcileAsync(onboardingId, includeRegistrationUrl: false, ct);
        }

        var error = sessionResult.IsFailure
            ? sessionResult.Error
            : new Error("Onboarding.SessionOnboardingMismatch", "Onboarding session onboarding id does not match.");
        return StatusCode(error.ToHttpStatusCode(), error);
    }

    private async Task<IActionResult> ReconcileAsync(
        Guid onboardingId,
        bool includeRegistrationUrl,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<ReconcileOnboardingPaymentResponse>>(
            new ReconcileOnboardingPaymentCommand(onboardingId, includeRegistrationUrl),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{onboardingId:guid}/cancel")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-cancel")]
    [RateLimitExempt("Anonymous onboarding cancellation keeps the native limiter; no JWT exists yet.")]
    public async Task<IActionResult> Cancel(Guid onboardingId, CancelOnboardingRequest? request, CancellationToken ct)
    {
        var sessionResult = await ValidateBoundSessionAsync(OnboardingSessionHttp.ReadToken(Request), onboardingId, ct);
        if (sessionResult.IsFailure)
            return StatusCode(sessionResult.Error.ToHttpStatusCode(), sessionResult.Error);

        var result = await bus.InvokeAsync<Result>(new CancelOnboardingCommand(onboardingId, request?.Reason), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private async Task<Result<OnboardingSession>> ValidateSessionForChallengeAsync(
        string? sessionToken,
        string email,
        Guid challengeId,
        CancellationToken ct
    )
    {
        var session = await sessions.ValidateAsync(sessionToken, DateTime.UtcNow, ct);
        if (session.IsFailure)
            return session;

        var matches = sessions.EnsureMatches(session.Value, email, challengeId);
        return matches.IsSuccess ? session : Result.Failure<OnboardingSession>(matches.Error);
    }

    private async Task<Result<OnboardingSession>> ValidateSessionForCheckoutAsync(
        string? sessionToken,
        Guid onboardingId,
        string email,
        CancellationToken ct
    )
    {
        var session = await sessions.ValidateAsync(sessionToken, DateTime.UtcNow, ct);
        if (session.IsFailure)
            return session;

        var matches = sessions.EnsureMatches(session.Value, onboardingId, email);
        return matches.IsSuccess ? session : Result.Failure<OnboardingSession>(matches.Error);
    }

    private async Task<Result<OnboardingSession>> ValidateBoundSessionAsync(
        string? sessionToken,
        Guid onboardingId,
        CancellationToken ct
    )
    {
        var session = await sessions.ValidateAsync(sessionToken, DateTime.UtcNow, ct);
        if (session.IsFailure)
            return session;

        var matches = sessions.EnsureBoundTo(session.Value, onboardingId);
        return matches.IsSuccess ? session : Result.Failure<OnboardingSession>(matches.Error);
    }
}
