using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Application.Notifications.Queries;
using TaxVision.Notification.Domain.Notifications;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

[ApiController]
[Route("notifications")]
[Authorize]
public sealed class NotificationsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Historial de notificaciones del tenant (email/SMS/in-app) para auditoría y soporte.</summary>
    [HttpGet]
    [Authorize(Roles = "TenantAdmin,PlatformAdmin")]
    [ProducesResponseType<PagedResult<NotificationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] NotificationStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PagedResult<NotificationResponse>>>(
            new GetNotificationsQuery(tenantId, status, page, size),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
