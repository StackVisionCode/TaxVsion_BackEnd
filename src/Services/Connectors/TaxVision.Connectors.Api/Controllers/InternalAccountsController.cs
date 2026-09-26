using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Connectors.Api.Requests;
using TaxVision.Connectors.Application.Accounts;
using Wolverine;

namespace TaxVision.Connectors.Api.Controllers;

/// <summary>
/// M2M interno — solo otro microservicio backend (Correspondence), nunca el frontend (política
/// "ServiceOnly", actor_type=Service). Resuelve qué buzones ve un usuario para que Correspondence
/// oculte el correo del buzón de oficina a quien no tiene office.read.
/// <para>
/// El tenant del cuerpo tiene que ser el del token: los tokens de servicio se emiten por tenant
/// (client-credentials con el tenant en la solicitud), así que un cuerpo con otro tenant es un
/// cruce, nunca un caso legítimo.
/// </para>
/// </summary>
[ApiController]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
[Route("internal/accounts")]
public sealed class InternalAccountsController(IMessageBus bus) : ControllerBase
{
    [HttpPost("visible-ids")]
    [RateLimitExempt("M2M interno ServiceOnly — mismo criterio que MessagesController (Connectors Fase 8).")]
    public async Task<IActionResult> VisibleIds([FromBody] VisibleAccountIdsRequest body, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(body.TenantId, out var tenantId))
            return Forbid();

        var ids = await bus.InvokeAsync<IReadOnlyList<Guid>>(
            new ListVisibleAccountIdsQuery(tenantId, body.UserId, body.IncludeOffice),
            ct
        );
        return Ok(new { accountIds = ids });
    }
}
