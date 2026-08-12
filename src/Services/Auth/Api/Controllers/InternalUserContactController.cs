using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Users.Internal.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Recuperación pull del correo de un usuario — M2M-only. Lo llama Notification cuando su directorio
/// <c>userId → email</c> no tiene fila para el destinatario (usuario registrado antes de que la
/// proyección existiera, o evento perdido), en vez de quedarse sin enviar el correo en silencio.
/// Mismo patrón, mismo policy y mismo motivo que <see cref="InternalPermissionsSnapshotController"/>.
/// </summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/users/{userId:guid}/contact")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalUserContactController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimitExempt(
        "M2M ServiceOnly (recuperación pull del directorio de correo) — nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<UserContactResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<UserContactResponse>>(new GetUserContactQuery(tenantId, userId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
