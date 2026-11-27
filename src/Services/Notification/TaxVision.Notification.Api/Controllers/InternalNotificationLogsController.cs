using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Application.Notifications.Queries;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// M2M-only (AJ-08, 03 §14.4): Auth lee el estado del correo de un link de reset al abrir el panel de la oficina.
/// Devuelve solo el estado. Bloqueado en el Gateway por el segmento <c>internal</c>.
/// </summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/notification-logs")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalNotificationLogsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("by-event/{eventId:guid}")]
    [RateLimitExempt("M2M ServiceOnly — lo invoca Auth al abrir el panel; nunca expuesto al Gateway.")]
    [ProducesResponseType<NotificationDeliveryStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByEvent(Guid tenantId, Guid eventId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();

        var result = await bus.InvokeAsync<Result<NotificationDeliveryStatusResponse>>(
            new GetNotificationDeliveryStatusQuery(tenantId, eventId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }
}
