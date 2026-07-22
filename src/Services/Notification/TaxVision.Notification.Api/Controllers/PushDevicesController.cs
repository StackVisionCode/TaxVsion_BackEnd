using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Application.Push.Commands;
using TaxVision.Notification.Domain.Notifications;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Registro/revocación de tokens de dispositivo push (FCM/APNs). Autoservicio:
/// no requiere un permiso especial, solo estar autenticado — el UserId sale
/// del JWT, nunca del body, así que un usuario solo puede tocar sus propios
/// tokens.
/// </summary>
[ApiController]
[Route("notifications/push/devices")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.CustomerPortal, ActorType.PlatformAdmin)]
public sealed class PushDevicesController(IMessageBus bus) : ControllerBase
{
    public sealed record RegisterRequest(PushPlatform Platform, string Token, string? DeviceId = null);

    [HttpPost]
    [ProducesResponseType<RegisterPushDeviceTokenResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var command = new RegisterPushDeviceTokenCommand(
            tenantId,
            userId,
            request.Platform,
            request.Token,
            request.DeviceId
        );
        var result = await bus.InvokeAsync<Result<RegisterPushDeviceTokenResult>>(command, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{tokenId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid tokenId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Unauthorized();

        var command = new RevokePushDeviceTokenCommand(tenantId, userId, tokenId);
        var result = await bus.InvokeAsync<Result>(command, ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
