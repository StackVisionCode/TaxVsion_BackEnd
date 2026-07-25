using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Postmaster.Application.Sending.Queries.GetSentMessageWithEvents;
using Wolverine;

namespace TaxVision.Postmaster.Api.Controllers;

[ApiController]
[Route("postmaster/messages")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class MessagesController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{id:guid}/events")]
    [HasPermission(PostmasterPermissions.MessagesRead)]
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
}
