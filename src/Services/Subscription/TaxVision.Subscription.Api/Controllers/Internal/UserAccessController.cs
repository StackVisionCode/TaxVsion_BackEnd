using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Internal.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Internal;

/// <summary>
/// Solo para llamadas service-to-service (token con actor_type=Service, ver policy
/// "ServiceOnly" en Program.cs). No se declara en el gateway público — Auth lo consulta
/// directamente en la red interna al emitir el JWT.
/// </summary>
[ApiController]
[Route("internal/users")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class UserAccessController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{userId:guid}/access")]
    [ProducesResponseType<UserAccessResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccess(Guid userId, [FromQuery] Guid tenantId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<UserAccessResponse>>(new GetUserAccessQuery(tenantId, userId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
