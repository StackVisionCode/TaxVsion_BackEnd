using System.Globalization;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Calendar.Application.Availability;
using TaxVision.Calendar.Application.Availability.Commands;
using TaxVision.Calendar.Application.Availability.Queries;
using TaxVision.Calendar.Domain.Availability;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

public sealed record AvailabilityRuleRequest(int DaysMask, string? StartTime, string? EndTime, string? TimeZoneId);

public sealed record ReplaceAvailabilityRulesRequest(IReadOnlyList<AvailabilityRuleRequest>? Rules);

public sealed record CreateBlockedTimeRequest(Guid? UserId, DateTime StartUtc, DateTime EndUtc, string? Reason);

/// <summary>
/// El horario semanal y las ausencias de cada persona, y el horario de la oficina. Cada uno maneja lo
/// suyo con <c>calendar.write</c>; lo de otra persona pide <c>calendar.availability.manage</c> (403).
/// </summary>
[ApiController]
[Route("calendar/availability")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class AvailabilityManagementController(IMessageBus bus, IUserPermissionsSource permissionsSource)
    : ControllerBase
{
    public static readonly Error TimeInvalid = new(
        "Calendar.Availability.TimeInvalid",
        "Times must use the HH:mm format, like 09:00."
    );

    [HttpGet("rules")]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.f.read")]
    public async Task<IActionResult> ListRules([FromQuery] Guid? userId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actingUserId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<AvailabilityRuleResponse>>>(
            new ListAvailabilityRulesQuery(tenantId, userId ?? actingUserId),
            ct
        );
        return Respond(result);
    }

    [HttpPut("rules")]
    [HasPermission(CalendarPermissions.Write)]
    [RateLimit("calendar.g.update")]
    public async Task<IActionResult> ReplaceRules(
        [FromQuery] Guid? userId,
        [FromBody] ReplaceAvailabilityRulesRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actingUserId))
            return Forbid();

        var inputs = new List<AvailabilityRuleInput>();
        foreach (var rule in request.Rules ?? [])
        {
            if (!TryTime(rule.StartTime, out var start) || !TryTime(rule.EndTime, out var end))
                return BadRequest(TimeInvalid);
            inputs.Add(new AvailabilityRuleInput(rule.DaysMask, start, end, rule.TimeZoneId));
        }

        var result = await bus.InvokeAsync<Result<IReadOnlyList<AvailabilityRuleResponse>>>(
            new ReplaceAvailabilityRulesCommand(
                tenantId,
                actingUserId,
                userId ?? actingUserId,
                await CanManageOthersAsync(ct),
                inputs
            ),
            ct
        );
        return Respond(result);
    }

    [HttpGet("blocks")]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.f.read")]
    public async Task<IActionResult> ListBlocks(
        [FromQuery] Guid? userId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actingUserId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<BlockedTimeResponse>>>(
            new ListBlockedTimesQuery(tenantId, userId ?? actingUserId, AsUtc(from), AsUtc(to)),
            ct
        );
        return Respond(result);
    }

    [HttpPost("blocks")]
    [HasPermission(CalendarPermissions.Write)]
    [RateLimit("calendar.g.create")]
    public async Task<IActionResult> CreateBlock([FromBody] CreateBlockedTimeRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actingUserId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<BlockedTimeResponse>>(
            new CreateBlockedTimeCommand(
                tenantId,
                actingUserId,
                request.UserId ?? actingUserId,
                await CanManageOthersAsync(ct),
                AsUtc(request.StartUtc),
                AsUtc(request.EndUtc),
                request.Reason
            ),
            ct
        );
        return Respond(result);
    }

    [HttpDelete("blocks/{blockId:guid}")]
    [HasPermission(CalendarPermissions.Write)]
    [RateLimit("calendar.g.delete")]
    public async Task<IActionResult> RemoveBlock(Guid blockId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actingUserId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new RemoveBlockedTimeCommand(tenantId, actingUserId, blockId, await CanManageOthersAsync(ct)),
            ct
        );
        if (result.IsSuccess)
            return NoContent();
        return result.Error == AvailabilityErrors.Forbidden
            ? Forbid()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Horario, cierres y zona de la oficina, para "Copy office hours" y los días cerrados.</summary>
    [HttpGet("office")]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.f.read")]
    public async Task<IActionResult> Office(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<OfficeScheduleResponse>>(new GetOfficeScheduleQuery(tenantId), ct);
        return Respond(result);
    }

    private Task<bool> CanManageOthersAsync(CancellationToken ct) =>
        permissionsSource.HasPermissionAsync(User, CalendarPermissions.AvailabilityManage, ct);

    private IActionResult Respond<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);
        return result.Error == AvailabilityErrors.Forbidden
            ? Forbid()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static bool TryTime(string? raw, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            raw?.Trim(),
            ["HH:mm", "HH:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time
        );

    // Sin zona se lee como UTC; con offset el binder ya la convirtió a hora local.
    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
