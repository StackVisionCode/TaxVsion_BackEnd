using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Customers.PortalSessions;
using TaxVision.Auth.Application.Sessions.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// M2M-only: Customer lista y cierra las sesiones del portal de un cliente para la oficina. El permiso y la
/// visibilidad del cliente los comprobó Customer antes de llamar.
/// </summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/customers/{customerId:guid}/portal-sessions")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalCustomerPortalSessionsController(IMessageBus bus, ICorrelationContext correlation)
    : ControllerBase
{
    [HttpGet]
    [RateLimitExempt(
        "M2M ServiceOnly — lo invoca Customer (con su propio rate limit); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<SessionPageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid tenantId,
        Guid customerId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();

        var result = await bus.InvokeAsync<Result<SessionPageResponse>>(
            new GetCustomerPortalSessionsQuery(tenantId, customerId, Utc(from), Utc(to), page, pageSize),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{sessionId:guid}")]
    [RateLimitExempt(
        "M2M ServiceOnly — lo invoca Customer (con su propio rate limit); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeOne(
        Guid tenantId,
        Guid customerId,
        Guid sessionId,
        [FromQuery] Guid requestedByUserId,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new RevokeCustomerPortalSessionCommand(
                tenantId,
                customerId,
                sessionId,
                requestedByUserId,
                correlation.CorrelationId
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete]
    [RateLimitExempt(
        "M2M ServiceOnly — lo invoca Customer (con su propio rate limit); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeAll(
        Guid tenantId,
        Guid customerId,
        [FromQuery] Guid requestedByUserId,
        [FromQuery] bool forgetTrustedDevices,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new RevokeCustomerPortalSessionsCommand(
                tenantId,
                customerId,
                requestedByUserId,
                forgetTrustedDevices,
                correlation.CorrelationId
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static DateTime? Utc(DateTime? value) =>
        value is null ? null
        : value.Value.Kind == DateTimeKind.Utc ? value
        : value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime()
        : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
}
