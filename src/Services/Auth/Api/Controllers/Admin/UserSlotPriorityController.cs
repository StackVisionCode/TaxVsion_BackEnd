using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.SlotGating.Commands;
using TaxVision.Auth.Application.SlotGating.Queries;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers.Admin;

[ApiController]
[AllowActorTypes(ActorType.TenantAdmin)]
[HasPermission(PermissionCatalog.SeatSlotGatingManage)]
public sealed class UserSlotPriorityController(IMessageBus bus) : ControllerBase
{
    public sealed record OverrideSlotPriorityRequest(DateTime? SlotPriorityAt, string Reason);

    [HttpPatch("auth/admin/users/{userId:guid}/slot-priority")]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("auth.g.seat_slot_priority_override")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Override(Guid userId, OverrideSlotPriorityRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var requesterId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new OverrideUserSlotPriorityCommand(
                tenantId,
                userId,
                requesterId,
                UserActorType.TenantAdmin,
                request.SlotPriorityAt,
                request.Reason
            ),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("auth/admin/tenants/me/seat-slots")]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("auth.f.user_read")]
    [ProducesResponseType<TenantSeatSlotsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReport(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantSeatSlotsResponse>>(new GetTenantSeatSlotsQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
