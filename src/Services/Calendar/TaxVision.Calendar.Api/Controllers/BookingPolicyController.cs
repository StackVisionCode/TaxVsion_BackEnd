using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Calendar.Application.Booking;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

public sealed record UpdateBookingPolicyRequest(
    bool Enabled,
    IReadOnlyList<Guid>? BookableTypeIds,
    int MinNoticeMinutes,
    int MaxDaysAhead,
    int BufferMinutes,
    int SlotStepMinutes,
    int CustomerChangeCutoffMinutes,
    bool RequiresApproval = true
);

/// <summary>Cómo reservan los clientes desde el portal. Es configuración de la firma.</summary>
[ApiController]
[Route("calendar/booking-policy")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class BookingPolicyController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.f.read")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<BookingPolicyResponse>>(new GetBookingPolicyQuery(tenantId), ct);
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPut]
    [HasPermission(CalendarPermissions.AvailabilityManage)]
    [RateLimit("calendar.g.update")]
    public async Task<IActionResult> Update([FromBody] UpdateBookingPolicyRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<BookingPolicyResponse>>(
            new UpdateBookingPolicyCommand(
                tenantId,
                userId,
                request.Enabled,
                request.BookableTypeIds ?? [],
                request.MinNoticeMinutes,
                request.MaxDaysAhead,
                request.BufferMinutes,
                request.SlotStepMinutes,
                request.CustomerChangeCutoffMinutes,
                request.RequiresApproval
            ),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }
}
