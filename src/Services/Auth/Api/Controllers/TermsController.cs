using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Terms.Commands;
using TaxVision.Auth.Application.Terms.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>Fase L1.4 — aceptacion del ToS/AUP vigente. Cualquier usuario autenticado del tenant puede aceptar (no requiere un permiso especifico) para evitar un bloqueo de arranque si el admin no esta disponible.</summary>
[ApiController]
[Route("auth/tenant/terms")]
[Authorize]
public sealed class TermsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<TermsAcceptanceStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<TermsAcceptanceStatusResponse>(
            new GetTermsAcceptanceStatusQuery(tenantId),
            ct
        );
        return Ok(result);
    }

    [HttpPost("accept")]
    [ProducesResponseType<TermsAcceptanceResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Accept(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<TermsAcceptanceResponse>(new AcceptTermsCommand(tenantId, userId), ct);
        return Ok(result);
    }
}
