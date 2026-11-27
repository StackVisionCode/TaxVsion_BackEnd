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
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// M2M-only (03 §14.3): Customer consulta, envía y cancela el link de reset del portal de un cliente para la oficina,
/// exige el cambio de contraseña y asegura la cuenta.
/// Customer ya comprobó permiso, visibilidad y step-up; Auth vuelve a aplicar permiso, política y ventana de
/// reautenticación. El actor y su <c>reauth_at</c> salen del token reenviado (SR-03), no del sobre. Bloqueado en el
/// Gateway por el segmento <c>internal</c>.
/// </summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/customers/{customerId:guid}/portal-credentials")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalCustomerPortalCredentialsController(
    IMessageBus bus,
    ICorrelationContext correlation,
    PortalCredentialActorGate actors
) : ControllerBase
{
    [HttpGet]
    [RateLimitExempt("M2M ServiceOnly — lo invoca Customer con su propio rate limit; nunca expuesto al Gateway.")]
    [ProducesResponseType<PortalCredentialStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid tenantId,
        Guid customerId,
        [FromQuery] Guid requestedByUserId,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();
        var actor = await actors.CheckAsync(
            Request,
            tenantId,
            customerId,
            requestedByUserId,
            CustomersPermissions.PortalCredentialsSendReset,
            PortalCredentialActorGate.ReadStatusModality,
            requireReauthentication: false,
            null,
            null,
            ct
        );
        if (actor.IsFailure)
            return StatusCode(actor.Error.ToHttpStatusCode(), actor.Error);

        var result = await bus.InvokeAsync<Result<PortalCredentialStatusResponse>>(
            new GetCustomerPortalCredentialStatusQuery(tenantId, customerId, actor.Value.UserId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("reset-link")]
    [RateLimitExempt("M2M ServiceOnly — lo invoca Customer con su propio rate limit; nunca expuesto al Gateway.")]
    [ProducesResponseType<ResetLinkIssuedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendResetLink(
        Guid tenantId,
        Guid customerId,
        PortalCredentialCommandEnvelope envelope,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();
        var checkedEnvelope = await CheckActorAsync(
            tenantId,
            customerId,
            envelope,
            CustomersPermissions.PortalCredentialsSendReset,
            OfficeCredentialAdministration.SendModality,
            ct
        );
        if (checkedEnvelope.IsFailure)
            return StatusCode(checkedEnvelope.Error.ToHttpStatusCode(), checkedEnvelope.Error);

        var result = await bus.InvokeAsync<Result<ResetLinkIssuedResponse>>(
            new SendCustomerPortalPasswordResetLinkCommand(
                tenantId,
                customerId,
                checkedEnvelope.Value,
                correlation.CorrelationId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("require-change")]
    [RateLimitExempt("M2M ServiceOnly — lo invoca Customer con su propio rate limit; nunca expuesto al Gateway.")]
    [ProducesResponseType<CredentialStateChangedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RequireChange(
        Guid tenantId,
        Guid customerId,
        [FromBody] PortalCredentialCommandEnvelope envelope,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();
        var checkedEnvelope = await CheckActorAsync(
            tenantId,
            customerId,
            envelope,
            CustomersPermissions.PortalCredentialsRequireChange,
            OfficeCredentialAdministration.RequireChangeModality,
            ct
        );
        if (checkedEnvelope.IsFailure)
            return StatusCode(checkedEnvelope.Error.ToHttpStatusCode(), checkedEnvelope.Error);

        var result = await bus.InvokeAsync<Result<CredentialStateChangedResponse>>(
            new RequireCustomerPortalPasswordChangeCommand(
                tenantId,
                customerId,
                checkedEnvelope.Value,
                correlation.CorrelationId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("secure")]
    [RateLimitExempt("M2M ServiceOnly — lo invoca Customer con su propio rate limit; nunca expuesto al Gateway.")]
    [ProducesResponseType<AccountSecuredResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Secure(
        Guid tenantId,
        Guid customerId,
        [FromBody] PortalCredentialCommandEnvelope envelope,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();
        var checkedEnvelope = await CheckActorAsync(
            tenantId,
            customerId,
            envelope,
            CustomersPermissions.PortalCredentialsSecure,
            OfficeCredentialAdministration.SecureModality,
            ct
        );
        if (checkedEnvelope.IsFailure)
            return StatusCode(checkedEnvelope.Error.ToHttpStatusCode(), checkedEnvelope.Error);

        var result = await bus.InvokeAsync<Result<AccountSecuredResponse>>(
            new SecureCustomerPortalAccountCommand(
                tenantId,
                customerId,
                checkedEnvelope.Value,
                correlation.CorrelationId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("reset-link")]
    [RateLimitExempt("M2M ServiceOnly — lo invoca Customer con su propio rate limit; nunca expuesto al Gateway.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelResetLink(
        Guid tenantId,
        Guid customerId,
        [FromBody] PortalCredentialCommandEnvelope envelope,
        CancellationToken ct
    )
    {
        if (!User.TryGetTenantId(out var tokenTenantId) || tokenTenantId != tenantId)
            return Forbid();
        var checkedEnvelope = await CheckActorAsync(
            tenantId,
            customerId,
            envelope,
            CustomersPermissions.PortalCredentialsSendReset,
            OfficeCredentialAdministration.CancelModality,
            ct
        );
        if (checkedEnvelope.IsFailure)
            return StatusCode(checkedEnvelope.Error.ToHttpStatusCode(), checkedEnvelope.Error);

        var result = await bus.InvokeAsync<Result>(
            new CancelCustomerPortalPasswordResetLinkCommand(
                tenantId,
                customerId,
                checkedEnvelope.Value,
                correlation.CorrelationId
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // El reauth_at del sobre se reemplaza por el del token firmado.
    private async Task<Result<PortalCredentialCommandEnvelope>> CheckActorAsync(
        Guid tenantId,
        Guid customerId,
        PortalCredentialCommandEnvelope envelope,
        string permission,
        string modality,
        CancellationToken ct
    )
    {
        var actor = await actors.CheckAsync(
            Request,
            tenantId,
            customerId,
            envelope.RequestedByUserId,
            permission,
            modality,
            requireReauthentication: true,
            envelope.ActorIpAddress,
            envelope.ActorUserAgent,
            ct
        );
        return actor.IsSuccess
            ? Result.Success(envelope with { ReauthenticatedAtUtc = actor.Value.ReauthenticatedAtUtc!.Value })
            : Result.Failure<PortalCredentialCommandEnvelope>(actor.Error);
    }
}
