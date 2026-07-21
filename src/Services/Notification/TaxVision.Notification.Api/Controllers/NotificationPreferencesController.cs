using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Application.Notifications.Preferences;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Fase 5 del plan de notificaciones dinámicas. Autoservicio: no requiere un permiso especial,
/// solo estar autenticado — el UserId sale del JWT, nunca del body (mismo patrón que
/// <see cref="PushDevicesController"/>), así que un usuario solo puede tocar sus propias
/// preferencias.
/// </summary>
[ApiController]
[Route("notifications/preferences")]
[Authorize]
public sealed class NotificationPreferencesController(IMessageBus bus) : ControllerBase
{
    public sealed record PreferenceResponse(string Category, string Channel, bool Enabled, bool Locked);

    public sealed record SetPreferenceRequest(NotificationCategory Category, NotificationChannel Channel, bool Enabled);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PreferenceResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var query = new GetNotificationPreferencesQuery(tenantId, userId);
        var result = await bus.InvokeAsync<Result<IReadOnlyList<NotificationPreferenceItem>>>(query, ct);
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var response = result
            .Value.Select(item => new PreferenceResponse(
                item.Category.ToString(),
                item.Channel.ToString(),
                item.Enabled,
                item.Locked
            ))
            .ToList();
        return Ok(response);
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Set([FromBody] SetPreferenceRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var command = new SetNotificationPreferenceCommand(
            tenantId,
            userId,
            request.Category,
            request.Channel,
            request.Enabled
        );
        var result = await bus.InvokeAsync<Result>(command, ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
