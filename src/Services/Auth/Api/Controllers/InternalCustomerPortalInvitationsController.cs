using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Security;
using TaxVision.Auth.Application.Customers.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// M2M-only: Customer pide el acceso al portal de un cliente y recibe el desenlace en la misma llamada
/// (invitado, reenviado o ya activo) o el motivo del rechazo, para que el CRM lo muestre. El actor, su tipo y
/// su permiso (con Deny) salen del token reenviado (SR-11), no de <c>RequestedByUserId</c>.
/// </summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/customers/{customerId:guid}/portal-invitation")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalCustomerPortalInvitationsController(IMessageBus bus, PortalCredentialActorGate actors)
    : ControllerBase
{
    // Los mismos actor types que admite Customer al pedir el acceso.
    private static readonly ForwardedActorRequirement Requirement = new(
        [ActorType.TenantAdmin, ActorType.PlatformAdmin],
        CustomersPermissions.Manage,
        RequireReauthentication: false
    );

    public sealed record IssuePortalInvitationRequest(
        string Email,
        Guid? RequestedByUserId,
        string? FirstName = null,
        string? LastName = null
    );

    [HttpPost]
    [RateLimitExempt(
        "M2M ServiceOnly — lo invoca Customer al pedir el acceso al portal; nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<PortalInvitationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Issue(
        Guid tenantId,
        Guid customerId,
        IssuePortalInvitationRequest request,
        CancellationToken ct
    )
    {
        // El token de servicio es por tenant: no puede operar sobre otra oficina.
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();
        var actor = await actors.CheckAsync(
            Request,
            tenantId,
            customerId,
            request.RequestedByUserId,
            Requirement,
            PortalCredentialActorGate.IssueInvitationModality,
            null,
            null,
            ct
        );
        if (actor.IsFailure)
            return StatusCode(actor.Error.ToHttpStatusCode(), actor.Error);

        var result = await bus.InvokeAsync<Result<PortalInvitationResult>>(
            new IssueCustomerPortalInvitationCommand(
                tenantId,
                customerId,
                request.Email,
                actor.Value.UserId,
                request.FirstName,
                request.LastName,
                actor.Value.ActorType.ToString()
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
