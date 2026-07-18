using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Application.TenantDomains.Commands;
using TaxVision.Auth.Application.TenantDomains.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>Fase A4 — disponibilidad de subdominio, llamable desde el apex/alta de oficina.</summary>
[ApiController]
[Route("auth/subdomains")]
public sealed class SubdomainsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("check-availability")]
    [AllowAnonymous]
    [EnableRateLimiting("tenant-lookup")]
    [ProducesResponseType<SubdomainAvailabilityResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckAvailability([FromQuery] string? slug, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SubdomainAvailabilityResponse>>(
            new CheckSubdomainAvailabilityQuery(slug),
            ct
        );
        return Ok(result.Value);
    }

    public sealed record ReserveSubdomainRequest(string? Slug, string? Email);

    /// <summary>Fase A7 — bloquea el slug para este email mientras el registro termina de completarse (TTL corto).</summary>
    [HttpPost("reserve")]
    [AllowAnonymous]
    [EnableRateLimiting("tenant-lookup")]
    [ProducesResponseType<SubdomainReservationResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Reserve(ReserveSubdomainRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SubdomainReservationResponse>>(
            new ReserveSubdomainCommand(request.Slug, request.Email),
            ct
        );

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
