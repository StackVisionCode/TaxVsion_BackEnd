using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Customers.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// M2M-only: Customer pide el acceso al portal de un cliente y recibe el desenlace en la misma llamada
/// (invitado, reenviado o ya activo) o el motivo del rechazo, para que el CRM lo muestre.
/// </summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/customers/{customerId:guid}/portal-invitation")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalCustomerPortalInvitationsController(IMessageBus bus) : ControllerBase
{
    public sealed record IssuePortalInvitationRequest(string Email, Guid? RequestedByUserId);

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

        var result = await bus.InvokeAsync<Result<PortalInvitationResult>>(
            new IssueCustomerPortalInvitationCommand(tenantId, customerId, request.Email, request.RequestedByUserId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
