using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Users;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Exposes recipient reconciliation data to trusted services.
/// </summary>
[ApiController]
[Route("internal/users")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalUserReconciliationController(IMessageBus bus) : ControllerBase
{
    [HttpGet("owners/reconciliation")]
    [RateLimitExempt("M2M interno de reconciliación (actor_type=Service, solo PlatformTenant); nunca en el Gateway.")]
    [ProducesResponseType<PagedResult<TenantOwnerReconciliationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Owners(
        [FromQuery] int page = 1,
        [FromQuery] int size = 200,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || tenantId != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<PagedResult<TenantOwnerReconciliationResponse>>(
            new ReconciliationTenantOwnersQuery(page, size),
            ct
        );
        return Ok(result);
    }

    [HttpGet("staff/reconciliation")]
    [RateLimitExempt("M2M interno de reconciliación (actor_type=Service, solo PlatformTenant); nunca en el Gateway.")]
    [ProducesResponseType<PagedResult<StaffUserReconciliationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Staff(
        [FromQuery] int page = 1,
        [FromQuery] int size = 200,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || tenantId != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<PagedResult<StaffUserReconciliationResponse>>(
            new ReconciliationStaffUsersQuery(page, size),
            ct
        );
        return Ok(result);
    }

    [HttpGet("portal-accounts/reconciliation")]
    [RateLimitExempt("M2M interno de reconciliación (actor_type=Service, solo PlatformTenant); nunca en el Gateway.")]
    [ProducesResponseType<PagedResult<CustomerPortalAccountReconciliationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PortalAccounts(
        [FromQuery] int page = 1,
        [FromQuery] int size = 200,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || tenantId != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<PagedResult<CustomerPortalAccountReconciliationResponse>>(
            new ReconciliationPortalAccountsQuery(page, size),
            ct
        );
        return Ok(result);
    }
}
