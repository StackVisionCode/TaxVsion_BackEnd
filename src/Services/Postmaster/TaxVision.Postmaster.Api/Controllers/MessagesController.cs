using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageDetail;
using TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageWithEvents;
using TaxVision.Postmaster.Application.Sending.Queries.ListSentMessages;
using Wolverine;

namespace TaxVision.Postmaster.Api.Controllers;

[ApiController]
[Route("postmaster/messages")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class MessagesController(IMessageBus bus) : ControllerBase
{
    /// <summary>"Email activity": historial de envíos del tenant con su estado real de entrega.</summary>
    [HttpGet]
    [HasPermission(PostmasterPermissions.MessagesRead)]
    [RateLimit("postmaster.f.messages_read")]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? recipient,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<PagedResult<SentMessageSummaryDto>>(
            new ListSentMessagesQuery(
                tenantId,
                new SentMessageActivityFilter(status, recipient, from, to),
                page,
                size,
                MaskRecipients()
            ),
            ct
        );
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(PostmasterPermissions.MessagesRead)]
    [RateLimit("postmaster.f.messages_read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<SentMessageDetailDto>>(
            new GetSentMessageDetailQuery(tenantId, id, MaskRecipients()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}/events")]
    [HasPermission(PostmasterPermissions.MessagesRead)]
    [RateLimit("postmaster.f.messages_read")]
    public async Task<IActionResult> GetEvents(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<SentMessageWithEventsDto>>(
            new GetSentMessageWithEventsQuery(tenantId, id),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>El empleado ve el historial para diagnosticar, pero sin las direcciones completas de los clientes.</summary>
    private bool MaskRecipients() => User.GetActorType() is not (ActorType.TenantAdmin or ActorType.PlatformAdmin);
}
