using System.Text.Json.Serialization;
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
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Application.Customers;
using TaxVision.Auth.Application.Customers.Commands;
using TaxVision.Auth.Domain.Credentials;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// M2M-only: Customer consulta y resetea el segundo factor del portal de un cliente para la oficina. El
/// permiso, la visibilidad del cliente y el step-up los comprobó Customer; Auth vuelve a validar permiso, ventana de
/// reautenticación y política con quien lo pidió, leído del token reenviado (SR-03).
/// </summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/customers/{customerId:guid}/portal-mfa")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalCustomerPortalMfaController(
    IMessageBus bus,
    ICorrelationContext correlation,
    PortalCredentialActorGate actors
) : ControllerBase
{
    /// <summary>Campos aditivos (03 §14.3). <see cref="ReauthenticatedAtUtc"/> ya no se usa: el step-up sale del token del actor (SR-03).</summary>
    public sealed record ResetPortalMfaRequest(
        Guid RequestedByUserId,
        DateTime? ReauthenticatedAtUtc = null,
        string? ActorIpAddress = null,
        string? ActorUserAgent = null,
        [property: JsonConverter(typeof(ReasonCodeJsonConverter))]
            CredentialAdministrationReasonCode? ReasonCode = null,
        string? ReasonNote = null,
        bool? IdentityConfirmed = null
    );

    [HttpGet]
    [RateLimitExempt("M2M ServiceOnly — lo invoca Customer al mostrar la ficha; nunca expuesto al Gateway público.")]
    [ProducesResponseType<CustomerPortalMfaStatus>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid tenantId, Guid customerId, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();

        var result = await bus.InvokeAsync<Result<CustomerPortalMfaStatus>>(
            new GetCustomerPortalMfaQuery(tenantId, customerId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("reset")]
    [RateLimitExempt(
        "M2M ServiceOnly — lo invoca Customer (con su propio rate limit); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<ResetCustomerPortalMfaResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reset(
        Guid tenantId,
        Guid customerId,
        ResetPortalMfaRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();
        var actor = await actors.CheckAsync(
            Request,
            tenantId,
            customerId,
            request.RequestedByUserId,
            CustomersPermissions.PortalMfaReset,
            OfficeCredentialAdministration.ResetMfaModality,
            requireReauthentication: true,
            request.ActorIpAddress,
            request.ActorUserAgent,
            ct
        );
        if (actor.IsFailure)
            return StatusCode(actor.Error.ToHttpStatusCode(), actor.Error);

        var result = await bus.InvokeAsync<Result<ResetCustomerPortalMfaResult>>(
            new ResetCustomerPortalMfaCommand(
                tenantId,
                customerId,
                new PortalCredentialCommandEnvelope(
                    actor.Value.UserId,
                    actor.Value.ReauthenticatedAtUtc!.Value,
                    request.ActorIpAddress,
                    request.ActorUserAgent,
                    request.ReasonCode,
                    request.ReasonNote,
                    request.IdentityConfirmed
                ),
                correlation.CorrelationId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
