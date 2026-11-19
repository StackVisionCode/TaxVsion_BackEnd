using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Customer.Application.Customers.Queries.Work;
using Wolverine;

namespace TaxVision.Customer.Api.Controllers;

/// <summary>Proveedor del Work Profile: clientes a cargo de una persona, con el ABAC del que mira.</summary>
[ApiController]
[Route("customers/work")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class CustomerWorkController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    // ---------- GET /customers/work/{userId} ----------
    [HttpGet("{userId:guid}")]
    [HasPermission(CustomersPermissions.View)]
    [RateLimit("customer.h.search")]
    [ProducesResponseType<WorkSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ForUser(Guid userId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorUserId))
            return Unauthorized();
        // Lo propio basta con customers.view; otra persona exige además users.work.view.
        if (userId != actorUserId && !await CanViewWorkAsync(ct))
            return StatusCode(StatusCodes.Status403Forbidden, CustomerWorkErrors.OtherUserForbidden);

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var work = await bus.InvokeAsync<WorkSummaryResponse>(
            new GetCustomerWorkQuery(tenantId, userId, actorUserId, canViewAll),
            ct
        );
        return Ok(work);
    }

    // ---------- GET /customers/work/summary?userIds=a,b,c ----------
    [HttpGet("summary")]
    [HasPermission(CustomersPermissions.View)]
    [RateLimit("customer.h.search")]
    [ProducesResponseType<WorkBatchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary([FromQuery] string? userIds, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorUserId))
            return Unauthorized();
        if (!await CanViewWorkAsync(ct))
            return StatusCode(StatusCodes.Status403Forbidden, CustomerWorkErrors.OtherUserForbidden);
        var ids = CustomerWorkUserIds.Parse(userIds);
        if (ids.IsFailure)
            return BadRequest(ids.Error);

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var batch = await bus.InvokeAsync<WorkBatchResponse>(
            new GetCustomerWorkBatchQuery(tenantId, ids.Value, actorUserId, canViewAll),
            ct
        );
        return Ok(batch);
    }

    private Task<bool> CanViewWorkAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, UserManagementPermissions.UsersWorkView, ct);
}
