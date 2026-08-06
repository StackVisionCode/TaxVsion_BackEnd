using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Permissions.Internal.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>Opción B (recuperación pull bajo demanda) — M2M-only: un microservicio consumidor en
/// modo <c>Authorization:PermissionsSource=Projection</c> llama este endpoint cuando encuentra un
/// miss local de proyección (usuario nunca sincronizado por ese servicio, típicamente porque se
/// sumó como consumidor después de que el backfill global de Auth ya corrió) y persiste el resultado
/// localmente — ver <c>ProjectionPermissionsSource</c> (BuildingBlocks.Web).</summary>
[ApiController]
[Route("auth/internal/tenants/{tenantId:guid}/users/{userId:guid}/permissions-snapshot")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalPermissionsSnapshotController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimitExempt("M2M ServiceOnly (Opción B, recuperación pull de permisos) — nunca expuesto al Gateway público.")]
    [ProducesResponseType<PermissionsSnapshotResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PermissionsSnapshotResponse>>(
            new GetPermissionsSnapshotQuery(tenantId, userId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
