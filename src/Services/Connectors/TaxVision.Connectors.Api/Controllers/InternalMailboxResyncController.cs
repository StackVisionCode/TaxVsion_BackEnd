using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Connectors.Api.Requests;
using TaxVision.Connectors.Application.Sync;
using Wolverine;

namespace TaxVision.Connectors.Api.Controllers;

/// <summary>
/// Re-sincronización de un buzón desde el proveedor (CON-07, runbook "Re-sincronizar buzones"). Solo
/// token de servicio; la ruta <c>/internal/...</c> no la publica el Gateway. Responde 202 y las páginas
/// corren en la cola local durable <c>mailbox-resync</c>.
/// </summary>
[ApiController]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
[Route("internal/connectors/accounts")]
public sealed class InternalMailboxResyncController(IMessageBus bus) : ControllerBase
{
    [HttpPost("{accountId:guid}/resync")]
    [RateLimitExempt("M2M interno ServiceOnly, lo lanza el runbook a mano.")]
    public async Task<IActionResult> Resync(Guid accountId, [FromBody] MailboxResyncRequest body, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(body.TenantId, out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new StartMailboxResyncCommand(tenantId, accountId, body.FromUtc, body.ToUtc),
            ct
        );
        if (result.IsSuccess)
            return Accepted();

        var status =
            result.Error.Code == StartMailboxResyncHandler.AccountNotActiveCode
                ? StatusCodes.Status409Conflict
                : result.Error.ToHttpStatusCode();
        return StatusCode(status, result.Error);
    }
}
