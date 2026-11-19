using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.TenantTimeZones;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Calendar.Application.Appointments.Work;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

/// <summary>
/// Proveedor de trabajo del Work Profile: próximas citas de una persona. La propia agenda con calendar.read; la de
/// otro exige users.work.view y además poder ver agendas ajenas. Los conteos usan la visibilidad del que mira.
/// </summary>
[ApiController]
[Route("calendar/work")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class WorkController(IMessageBus bus, IUserPermissionsSource permissions, ITenantTimeZoneResolver zones)
    : ControllerBase
{
    [HttpGet("{userId:guid}")]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.h.range")]
    [ProducesResponseType<WorkSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForUser(Guid userId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Forbid();

        var rights = await RightsAsync(ct);
        if (!WorkAccess.CanSee(actorId, userId, rights))
            return Forbid();

        var zone = await zones.ResolveAsync(tenantId, ct);
        var result = await bus.InvokeAsync<Result<WorkSummaryResponse>>(
            new GetAppointmentWorkQuery(tenantId, userId, actorId, rights.CanViewAllCustomers, zone, DateTime.UtcNow),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>Conteos de los próximos 7 días de la oficina por persona (<c>userIds=a,b,c</c>, máximo 100).</summary>
    [HttpGet("summary")]
    [HasPermission(CalendarPermissions.Read)]
    [HasPermission(UserManagementPermissions.UsersWorkView)]
    [RateLimit("calendar.h.range")]
    [ProducesResponseType<WorkBatchResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] string? userIds, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Forbid();

        var rights = await RightsAsync(ct);
        if (!WorkAccess.CanSeeOthers(rights))
            return Forbid();

        var ids = WorkUserIds.Parse(userIds);
        if (ids.IsFailure)
            return StatusCode(StatusCodes.Status400BadRequest, ids.Error);

        var zone = await zones.ResolveAsync(tenantId, ct);
        var result = await bus.InvokeAsync<Result<WorkBatchResponse>>(
            new GetAppointmentWorkBatchQuery(
                tenantId,
                ids.Value,
                actorId,
                rights.CanViewAllCustomers,
                zone,
                DateTime.UtcNow
            ),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>Títulos en vivo de citas para Activity: solo lo que quien mira puede leer; lo demás se omite.</summary>
    [HttpPost("titles")]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.h.range")]
    [ProducesResponseType<WorkTitlesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Titles([FromBody] WorkTitlesRequest? request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Forbid();
        var refs = WorkTitleRefs.Validate(request);
        if (refs.IsFailure)
            return StatusCode(StatusCodes.Status400BadRequest, refs.Error);

        var ids = WorkTitleRefs.IdsOf(refs.Value, WorkResourceTypes.Appointment);
        if (ids.Count == 0)
            return Ok(new WorkTitlesResponse([]));

        var canViewAll = await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var titles = await bus.InvokeAsync<IReadOnlyList<WorkTitle>>(
            new GetAppointmentWorkTitlesQuery(tenantId, actorId, canViewAll, ids),
            ct
        );
        return Ok(new WorkTitlesResponse(titles));
    }

    /// <summary>"Show all" de las próximas citas: misma autorización, universo y orden que <c>/work/{userId}</c>.</summary>
    [HttpGet("{userId:guid}/items")]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.h.range")]
    [ProducesResponseType<PagedResult<WorkItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Items(
        Guid userId,
        [FromQuery] string? kind,
        [FromQuery] int? page,
        [FromQuery] int? size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Forbid();

        var rights = await RightsAsync(ct);
        if (!WorkAccess.CanSee(actorId, userId, rights))
            return Forbid();
        if (kind != WorkKinds.Appointments)
            return StatusCode(StatusCodes.Status400BadRequest, WorkErrors.InvalidKind);

        var paging = WorkPaging.Normalize(page, size);
        var zone = await zones.ResolveAsync(tenantId, ct);
        var result = await bus.InvokeAsync<Result<PagedResult<WorkItem>>>(
            new GetAppointmentWorkItemsQuery(
                tenantId,
                userId,
                actorId,
                rights.CanViewAllCustomers,
                zone,
                DateTime.UtcNow,
                paging.Page,
                paging.Size
            ),
            ct
        );
        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    private async Task<WorkViewerRights> RightsAsync(CancellationToken ct) =>
        new(
            await permissions.HasPermissionAsync(User, UserManagementPermissions.UsersWorkView, ct),
            await permissions.HasPermissionAsync(User, CalendarPermissions.ManageAll, ct),
            await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct)
        );
}
