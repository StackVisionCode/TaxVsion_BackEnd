using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Onboarding.PaymentOptions;
using TaxVision.Auth.Application.Onboarding.Sessions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow entry point: creates the pre-tenant onboarding and starts checkout.</summary>
[ApiController]
[Route("onboarding")]
public sealed class OnboardingCheckoutController(IMessageBus bus, OnboardingSessionService sessions) : ControllerBase
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
    [EnableRateLimiting("onboarding-checkout-create")]
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
    [EnableRateLimiting("onboarding-checkout-create")]
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

    public sealed record CancelOnboardingRequest(string? Reason = null);

    public sealed record ReconcilePaymentRequest();

    [HttpGet("payment-options")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-checkout-create")]
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
    [EnableRateLimiting("onboarding-checkout-create")]
    [RateLimitExempt(
        "Anonymous onboarding payment reconcile keeps the native limiter; a bound post-OTP session is required."
    )]
    [ProducesResponseType<ReconcileOnboardingPaymentResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReconcilePayment(ReconcilePaymentRequest? request, CancellationToken ct)
    {
        var sessionResult = await sessions.ValidateAsync(OnboardingSessionHttp.ReadToken(Request), DateTime.UtcNow, ct);
        if (sessionResult.IsFailure)
            return StatusCode(sessionResult.Error.ToHttpStatusCode(), sessionResult.Error);

        if (sessionResult.Value.OnboardingId is null)
        {
            var error = new Error(
                "Onboarding.SessionOnboardingMismatch",
                "Onboarding session onboarding id does not match."
            );
            return StatusCode(error.ToHttpStatusCode(), error);
        }

        var result = await bus.InvokeAsync<Result<ReconcileOnboardingPaymentResponse>>(
            new ReconcileOnboardingPaymentCommand(sessionResult.Value.OnboardingId.Value),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{onboardingId:guid}/cancel")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-checkout-create")]
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
