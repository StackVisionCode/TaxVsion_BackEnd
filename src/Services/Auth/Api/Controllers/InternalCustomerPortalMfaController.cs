using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
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
/// M2M-only: Customer consulta y resetea el segundo factor del portal de un cliente para la oficina. El
/// permiso y la visibilidad del cliente los comprobó Customer antes de llamar.
/// </summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/customers/{customerId:guid}/portal-mfa")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalCustomerPortalMfaController(IMessageBus bus, ICorrelationContext correlation)
    : ControllerBase
{
    public sealed record ResetPortalMfaRequest(Guid RequestedByUserId);

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

        var result = await bus.InvokeAsync<Result<ResetCustomerPortalMfaResult>>(
            new ResetCustomerPortalMfaCommand(
                tenantId,
                customerId,
                request.RequestedByUserId,
                correlation.CorrelationId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
