using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
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
/// </summary>
[ApiController]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
[Route("connectors/internal/accounts")]
public sealed class InternalAccountsController(IMessageBus bus) : ControllerBase
{
    [HttpPost("visible-ids")]
    [RateLimitExempt("M2M interno ServiceOnly — mismo criterio que MessagesController (Connectors Fase 8).")]
    public async Task<IActionResult> VisibleIds([FromBody] VisibleAccountIdsRequest body, CancellationToken ct)
    {
        var ids = await bus.InvokeAsync<IReadOnlyList<Guid>>(
            new ListVisibleAccountIdsQuery(body.TenantId, body.UserId, body.IncludeOffice),
            ct
        );
        return Ok(new { accountIds = ids });
    }
}
