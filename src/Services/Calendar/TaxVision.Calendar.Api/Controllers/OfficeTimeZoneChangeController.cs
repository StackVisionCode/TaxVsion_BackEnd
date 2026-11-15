using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Calendar.Application.Office.TimeZoneChanges;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

public sealed record ApplyOfficeTimeZoneChangeRequest(IReadOnlyList<Guid>? RuleIds, IReadOnlyList<Guid>? SeriesIds);

/// <summary>
/// P9: el cambio de zona de la oficina pendiente y la vista previa de lo que se puede mover. Mover horarios y series de
/// otras personas es tarea de admin: <c>calendar.availability.manage</c>.
/// </summary>
[ApiController]
[Route("calendar/office/time-zone-change")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class OfficeTimeZoneChangeController(IMessageBus bus) : ControllerBase
{
    [HttpPost("{changeId:guid}/apply")]
    [HasPermission(CalendarPermissions.AvailabilityManage)]
    [RateLimit("calendar.g.update")]
    public async Task<IActionResult> Apply(
        Guid changeId,
        [FromBody] ApplyOfficeTimeZoneChangeRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actingUserId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new ApplyOfficeTimeZoneChangeCommand(
                tenantId,
                actingUserId,
                changeId,
                request.RuleIds ?? [],
                request.SeriesIds ?? [],
                DateTime.UtcNow
            ),
            ct
        );
        return Respond(result);
    }

    [HttpPost("{changeId:guid}/dismiss")]
    [HasPermission(CalendarPermissions.AvailabilityManage)]
    [RateLimit("calendar.g.update")]
    public async Task<IActionResult> Dismiss(Guid changeId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actingUserId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new DismissOfficeTimeZoneChangeCommand(tenantId, actingUserId, changeId, DateTime.UtcNow),
            ct
        );
        return Respond(result);
    }

    // Ya decidido o reemplazado: 409, el cliente recarga la vista previa.
    private IActionResult Respond(Result result) =>
        result.IsSuccess ? NoContent()
        : result.Error == TaxVision.Calendar.Domain.Office.OfficeTimeZoneChangeErrors.NotPending
            ? Conflict(result.Error)
        : BadRequest(result.Error);

    [HttpGet]
    [HasPermission(CalendarPermissions.AvailabilityManage)]
    [RateLimit("calendar.f.read")]
    public async Task<IActionResult> Preview(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<OfficeTimeZoneChangePreview?>>(
            new GetOfficeTimeZoneChangePreviewQuery(tenantId, DateTime.UtcNow),
            ct
        );
        if (result.IsFailure)
            return BadRequest(result.Error);
        return result.Value is null ? NoContent() : Ok(result.Value);
    }
}
