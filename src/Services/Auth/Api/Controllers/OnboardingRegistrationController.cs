using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Application.Onboarding.Registration.Commands;
using TaxVision.Auth.Application.Onboarding.Registration.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow (Fase 13) — canjea el RegistrationToken opaco enviado por email: preview del
/// comprador antes de mostrar el form, y submit final que arranca el provisioning (Fase 15).
/// Anónimo por diseño — el comprador todavía no tiene sesión ni tenant.</summary>
[ApiController]
[Route("onboarding/register")]
public sealed class OnboardingRegistrationController(IMessageBus bus) : ControllerBase
{
    public sealed record PreviewRequest(string Token);

    [HttpPost("preview")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-registration-preview")]
    [RateLimitExempt(
        "Anónimo (Fase 13) — conserva el limiter nativo onboarding-registration-preview, sin JWT que particionar."
    )]
    [ProducesResponseType<PreviewRegistrationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(PreviewRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PreviewRegistrationResponse>>(
            new PreviewRegistrationQuery(request.Token),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CompleteRequest(
        string Token,
        string Password,
        string OfficeName,
        string Subdomain,
        bool TermsAccepted,
        Guid TermsVersionId
    );

    [HttpPost("complete")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-registration-complete")]
    [RateLimitExempt(
        "Anónimo (Fase 13) — conserva el limiter nativo onboarding-registration-complete, sin JWT que particionar."
    )]
    [ProducesResponseType<CompleteOnboardingRegistrationResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Complete(CompleteRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CompleteOnboardingRegistrationResponse>>(
            new CompleteOnboardingRegistrationCommand(
                request.Token,
                request.Password,
                request.OfficeName,
                request.Subdomain,
                request.TermsAccepted,
                request.TermsVersionId
            ),
            ct
        );

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return StatusCode(StatusCodes.Status202Accepted, result.Value);
    }
}
