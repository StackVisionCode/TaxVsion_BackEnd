using BuildingBlocks.Results;
using BuildingBlocks.Web.Platform;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Platform;
using TaxVision.Tenant.Application.Platform.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Lectura ANÓNIMA de la marca y el dominio de la plataforma. La usan los demás servicios al arrancar
/// (<see cref="PlatformSettingsRefreshService"/>, por la red interna) y la pueden usar los frontends
/// para no llevar el dominio compilado. No hay nada privado: es lo que ya sale en cada correo.
/// </summary>
[ApiController]
[Route(PlatformSettingsRefreshService.PublicPath)]
[AllowAnonymous]
public sealed class PublicPlatformSettingsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimitExempt(
        "Recurso único e igual para todos, sin token que particionar; lo leen los servicios por la red "
            + "interna cada pocos minutos."
    )]
    [ProducesResponseType<PlatformSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PlatformSettingsResponse>>(new GetPlatformSettingsQuery(), ct);
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        Response.Headers.CacheControl = "public, max-age=60";
        return Ok(result.Value);
    }
}
