using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.EmailVerification.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>Verificación de email por OTP para el signup pago-primero (PayFlow Fase 5).</summary>
[ApiController]
[Route("onboarding/email-challenges")]
public sealed class OnboardingChallengesController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateChallengeRequest(string Email, string? FirstNameHint = null);

    public sealed record VerifyChallengeRequest(string Code);

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-email-challenge")]
    [RateLimitExempt(
        "Anónimo (F22) — conserva el limiter nativo onboarding-email-challenge + ILoginThrottler.AuthorizeOnboardingChallengeCreationAsync (dominio), sin JWT que particionar."
    )]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        CreateChallengeRequest request,
        [FromServices] IRequestContext requestContext,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<Guid>>(
            new CreateEmailChallengeCommand(
                request.Email,
                requestContext.IpAddress ?? "unknown",
                request.FirstNameHint
            ),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return StatusCode(StatusCodes.Status201Created, new { challengeId = result.Value });
    }

    [HttpPost("{challengeId:guid}/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-email-challenge")]
    [RateLimitExempt("Anónimo (F22) — conserva el limiter nativo onboarding-email-challenge, sin JWT que particionar.")]
    [ProducesResponseType<VerifyEmailChallengeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Verify(Guid challengeId, VerifyChallengeRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<VerifyEmailChallengeResponse>>(
            new VerifyEmailChallengeCommand(challengeId, request.Code),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        OnboardingSessionHttp.AppendCookie(Response, result.Value.SessionToken, result.Value.ExpiresAtUtc);
        return Ok(result.Value);
    }

    [HttpPost("{challengeId:guid}/resend")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-email-challenge")]
    [RateLimitExempt(
        "Anónimo (F22) — conserva el limiter nativo onboarding-email-challenge + ILoginThrottler.AuthorizeOnboardingResendAsync (dominio), sin JWT que particionar."
    )]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Resend(Guid challengeId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(new ResendEmailChallengeCommand(challengeId), ct);
        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
