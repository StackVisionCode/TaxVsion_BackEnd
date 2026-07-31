using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Application.Onboarding.SubdomainReservations.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow (Fase 14) — chequea y reserva (TTL 60min) el slug de subdominio elegido
/// durante el registro post-pago. Anónimo por diseño (mismo momento del flujo que
/// OnboardingRegistrationController).</summary>
[ApiController]
[Route("onboarding/subdomains")]
public sealed class OnboardingSubdomainController(IMessageBus bus) : ControllerBase
{
    public sealed record CheckSubdomainRequest(string Slug, string Token);

    [HttpPost("check")]
    [AllowAnonymous]
    [EnableRateLimiting("onboarding-subdomain-check")]
    [ProducesResponseType<SubdomainReservationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Check(CheckSubdomainRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SubdomainReservationResponse>>(
            new ReserveSubdomainForOnboardingCommand(request.Slug, request.Token),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
