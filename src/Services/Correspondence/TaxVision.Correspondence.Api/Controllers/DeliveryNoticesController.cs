using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Api.Requests;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Delivery;
using TaxVision.Correspondence.Application.Messages;
using Wolverine;

namespace TaxVision.Correspondence.Api.Controllers;

/// <summary>
/// Avisos de entrega de los correos enviados desde el CRM. Se listan dentro de la conversación
/// (<c>GET /threads/{id}/messages</c>, <c>Kind = DeliveryNotice</c>) y se marcan leídos con el hilo; acá viven
/// "View original notice" y el reenvío. TenantId siempre del JWT.
/// </summary>
[ApiController]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class DeliveryNoticesController(
    IMessageBus bus,
    IMailboxVisibilityResolver visibility,
    ICustomerAccessResolver customerAccess
) : ControllerBase
{
    /// <summary>El aviso técnico original, en vivo desde el buzón donde llegó. 404 si no se ve; 409 si vino por webhook.</summary>
    [HttpGet("correspondence/delivery-notices/{id:guid}/original")]
    [HasPermission(CorrespondencePermissions.Read)]
    [RateLimit("correspondence.f.message_read")]
    [ProducesResponseType<MessageBodyResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOriginal(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var visible = await visibility.ResolveVisibleAccountIdsAsync(User, tenantId, ct);
        var restrictTo = await customerAccess.ResolveRestrictedUserIdAsync(User, ct);
        var result = await bus.InvokeAsync<Result<MessageBodyResult>>(
            new GetDeliveryNoticeOriginalQuery(tenantId, id, visible, restrictTo),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }

    /// <summary>
    /// Crea un borrador para reenviar el correo que rebotó (mismo asunto, cuerpo y adjuntos) solo a ese destinatario
    /// o a la dirección corregida, en la misma conversación. Devuelve <c>{ draftId }</c>; el usuario lo revisa y envía.
    /// </summary>
    [HttpPost("correspondence/delivery-notices/{id:guid}/resend")]
    [HasPermission(CorrespondencePermissions.Compose)]
    [RateLimit("correspondence.g.draft_manage")]
    public async Task<IActionResult> Resend(Guid id, [FromBody] ResendDeliveryNoticeBody? body, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var userId))
            return Forbid();

        var visible = await visibility.ResolveVisibleAccountIdsAsync(User, tenantId, ct);
        var restrictTo = await customerAccess.ResolveRestrictedUserIdAsync(User, ct);
        var result = await bus.InvokeAsync<Result<ResendDeliveryNoticeResult>>(
            new ResendDeliveryNoticeCommand(tenantId, id, body?.Recipient, userId, visible, restrictTo),
            ct
        );
        return result.IsSuccess ? Ok(new { draftId = result.Value.DraftId }) : ErrorResult(result.Error);
    }

    // Códigos propios de este servicio: el mapeo compartido no los conoce.
    private ObjectResult ErrorResult(Error error)
    {
        var status = error.Code switch
        {
            "DeliveryNotice.NotFound" => StatusCodes.Status404NotFound,
            "DeliveryNotice.OriginalUnavailable" => StatusCodes.Status409Conflict,
            _ => error.ToHttpStatusCode(),
        };
        return StatusCode(status, error);
    }
}
