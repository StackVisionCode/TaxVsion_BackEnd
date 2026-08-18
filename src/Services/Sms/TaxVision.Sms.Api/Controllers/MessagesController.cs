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
public sealed class MessagesController(IMessageBus bus, ITenantContext tenant, ICorrelationContext correlation)
    : ControllerBase
{
    public sealed record MediaItemRequest(string Url, string ContentType, string? FileName, long? SizeBytes);

    public sealed record MessageItemRequest(
        Guid CustomerId,
        string To,
        string Message,
        IReadOnlyList<MediaItemRequest>? Media,
        string? IdempotencyKey,
        string? SourceContext
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
                m.SourceContext
            ))
            .ToList();

        var result = await bus.InvokeAsync<Result<SendSmsBatchResponse>>(
            new SendSmsBatchCommand(tenant.TenantId, correlation.CorrelationId, items),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
