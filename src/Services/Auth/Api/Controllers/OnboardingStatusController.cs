using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Application.Onboarding.Registration.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow (Fase 13) — polling público del progreso de provisioning tras el submit del
/// form de registro. Anónimo, sin exponer nunca el OnboardingId.</summary>
[ApiController]
[Route("onboarding/status")]
public sealed class OnboardingStatusController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-status")]
    [RateLimitExempt("Anónimo (Fase 13) — conserva el limiter nativo onboarding-status, sin JWT que particionar.")]
    [ProducesResponseType<OnboardingStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] string token, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<OnboardingStatusResponse>>(new GetOnboardingStatusQuery(token), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
