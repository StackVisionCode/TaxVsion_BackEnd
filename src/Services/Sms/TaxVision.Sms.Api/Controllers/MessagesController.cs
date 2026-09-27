using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Sms.Application.Messages.Commands;
using Wolverine;

namespace TaxVision.Sms.Api.Controllers;

/// <summary>Punto de entrada del diagrama: cualquier microservicio (M2M) o usuario autenticado envía
/// 1..N mensajes. El tenant se toma del JWT — nunca de un campo del caller.</summary>
[ApiController]
[Route("sms")]
[Authorize]
[AllowActorTypes(ActorType.Service, ActorType.TenantAdmin, ActorType.TenantEmployee)]
public sealed class MessagesController(
    IMessageBus bus,
    ITenantContext tenant,
    ICorrelationContext correlation,
    IUserPermissionsSource permissions
) : ControllerBase
{
    public sealed record MediaItemRequest(string Url, string ContentType, string? FileName, long? SizeBytes);

    public sealed record MessageItemRequest(
        Guid CustomerId,
        string To,
        string Message,
        IReadOnlyList<MediaItemRequest>? Media,
        string? IdempotencyKey,
        string? SourceContext,
        string? RecipientName
    );

    public sealed record SendMessagesRequest(IReadOnlyList<MessageItemRequest> Messages);

    [HttpPost("messages")]
    [HasPermission(SmsPermissions.Send)]
    [RateLimit("sms.h.send")]
    [ProducesResponseType<SendSmsBatchResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send([FromBody] SendMessagesRequest request, CancellationToken ct)
    {
        var items = (request.Messages ?? [])
            .Select(m => new SmsSendItemDto(
                m.CustomerId,
                m.To,
                m.Message,
                m.Media?.Select(x => new SmsMediaDto(x.Url, x.ContentType, x.FileName, x.SizeBytes)).ToList(),
                m.IdempotencyKey,
                m.SourceContext,
                m.RecipientName
            ))
            .ToList();

        // El endpoint lo comparten los servicios (M2M) y las personas. Solo a una persona se le mide el
        // alcance: un servicio no tiene cartera de clientes, y la campaña que dispara el envío ya
        // resolvió la suya.
        Guid? actorUserId =
            User.GetActorType() == ActorType.Service || !User.TryGetUserId(out var userId) ? null : userId;
        var canViewAll =
            actorUserId is not null && await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);

        var result = await bus.InvokeAsync<Result<SendSmsBatchResponse>>(
            new SendSmsBatchCommand(tenant.TenantId, correlation.CorrelationId, items, actorUserId, canViewAll),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
