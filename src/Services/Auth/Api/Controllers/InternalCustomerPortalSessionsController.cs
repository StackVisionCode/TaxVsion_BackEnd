using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Security;
using TaxVision.Auth.Application.Customers.PortalSessions;
using TaxVision.Auth.Application.Sessions.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// M2M-only: Customer lista y cierra las sesiones del portal de un cliente para la oficina. La visibilidad del
/// cliente la comprobó Customer; el actor, su tipo y su permiso (con Deny) salen del token reenviado (SR-11).
/// </summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/customers/{customerId:guid}/portal-sessions")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalCustomerPortalSessionsController(
    IMessageBus bus,
    ICorrelationContext correlation,
    PortalCredentialActorGate actors
) : ControllerBase
{
    // Los mismos actor types que admite Customer en la ficha.
    private static readonly ActorType[] OfficeActors =
    [
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
    ];

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
        var actor = await actors.CheckAsync(
            Request,
            tenantId,
            customerId,
            requestedByUserId: null,
            new ForwardedActorRequirement(OfficeActors, CustomersPermissions.View, RequireReauthentication: false),
            PortalCredentialActorGate.ListSessionsModality,
            null,
            null,
            ct
        );
        if (actor.IsFailure)
            return StatusCode(actor.Error.ToHttpStatusCode(), actor.Error);

        // Sin el permiso de ubicación, Auth tampoco la entrega (Customer la vuelve a ocultar con el suyo).
        var showLocation = await actors.HasPermissionAsync(
            actor.Value,
            CustomersPermissions.PortalSessionsLocationView,
            ct
        );
        var result = await bus.InvokeAsync<Result<SessionPageResponse>>(
            new GetCustomerPortalSessionsQuery(tenantId, customerId, Utc(from), Utc(to), page, pageSize, showLocation),
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
        var actor = await CheckManageAsync(
            tenantId,
            customerId,
            requestedByUserId,
            PortalCredentialActorGate.RevokeSessionModality,
            ct
        );
        if (actor.IsFailure)
            return StatusCode(actor.Error.ToHttpStatusCode(), actor.Error);

        var result = await bus.InvokeAsync<Result>(
            new RevokeCustomerPortalSessionCommand(
                tenantId,
                customerId,
                sessionId,
                actor.Value.UserId,
                correlation.CorrelationId,
                actor.Value.ActorType.ToString()
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
        var actor = await CheckManageAsync(
            tenantId,
            customerId,
            requestedByUserId,
            PortalCredentialActorGate.RevokeAllSessionsModality,
            ct
        );
        if (actor.IsFailure)
            return StatusCode(actor.Error.ToHttpStatusCode(), actor.Error);

        var result = await bus.InvokeAsync<Result>(
            new RevokeCustomerPortalSessionsCommand(
                tenantId,
                customerId,
                actor.Value.UserId,
                forgetTrustedDevices,
                correlation.CorrelationId,
                actor.Value.ActorType.ToString()
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // Cerrar sesiones: customers.manage, sin step-up (igual que en Customer).
    private Task<Result<ForwardedActor>> CheckManageAsync(
        Guid tenantId,
        Guid customerId,
        Guid requestedByUserId,
        string modality,
        CancellationToken ct
    ) =>
        actors.CheckAsync(
            Request,
            tenantId,
            customerId,
            requestedByUserId,
            new ForwardedActorRequirement(OfficeActors, CustomersPermissions.Manage, RequireReauthentication: false),
            modality,
            null,
            null,
            ct
        );

    private static DateTime? Utc(DateTime? value) =>
        value is null ? null
        : value.Value.Kind == DateTimeKind.Utc ? value
        : value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime()
        : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
}
