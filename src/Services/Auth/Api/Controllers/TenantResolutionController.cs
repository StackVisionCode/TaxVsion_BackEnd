using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.TenantDomains.Commands;
using TaxVision.Auth.Application.Tenants.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>Fase A4 — endpoints públicos de resolución de tenant desde subdominio.</summary>
[ApiController]
[Route("auth/tenant-resolution")]
public sealed class TenantResolutionController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// El frontend, servido en el subdominio de la oficina, llama esto al cargar el
    /// login para fijar el TenantId. El Host ya fue resuelto por
    /// TenantHostResolutionMiddleware — este endpoint solo expone ese candidato; si no
    /// resolvió (host desconocido/apex), 404.
    /// </summary>
    [HttpGet("by-host")]
    [AllowAnonymous]
    [EnableRateLimiting("tenant-lookup")]
    [ProducesResponseType<TenantResolutionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ByHost([FromServices] IResolvedTenantContext tenantContext, CancellationToken ct)
    {
        if (tenantContext.ResolvedTenantId is not { } tenantId)
            return NotFound();

        var result = await bus.InvokeAsync<Result<TenantResolutionResponse>>(
            new GetTenantPublicInfoQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }

    public sealed record TenantRecoveryRequest(string Email);

    /// <summary>
    /// "Encuentra tu oficina" (estilo Slack): envía por correo los subdominios donde
    /// el email tiene cuenta activa. Llamable desde el apex (nunca resuelve tenant).
    /// Siempre 202: ni la existencia del email ni cuántas oficinas encontró se revelan
    /// en la respuesta (anti-enumeración) — el resultado real solo llega por email.
    /// </summary>
    [HttpPost("by-email")]
    [AllowAnonymous]
    [EnableRateLimiting("tenant-recovery")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ByEmail(TenantRecoveryRequest request, CancellationToken ct)
    {
        await bus.InvokeAsync<Result>(new RequestTenantRecoveryCommand(request.Email), ct);
        return Accepted();
    }
}
