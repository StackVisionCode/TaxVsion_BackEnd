using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Reminder.Api.Requests;
using TaxVision.Reminder.Application.Reminders;
using TaxVision.Reminder.Application.Reminders.Commands;
using TaxVision.Reminder.Application.Reminders.Queries;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Reminder.Api.Controllers;

/// <summary>
/// Las 5 capas en cada acción: <c>[Authorize]</c> (global) + <c>[AllowActorTypes]</c> +
/// <c>[HasPermission]</c> + <c>[RateLimit]</c> + el filtro global de tenant del DbContext.
///
/// <para>
/// <b>Sin <c>CustomerPortal</c>.</b> Un recordatorio pertenece a un usuario del tenant (invariante
/// R1); el cliente final no tiene recordatorios propios en v1, así que tampoco hay controller de
/// portal — a diferencia de Notes.
/// </para>
///
/// <para>
/// <b>Sin permiso de gobernanza.</b> Ni <c>PlatformAdmin</c> lee recordatorios ajenos: no existe un
/// <c>reminders.view_all</c>. El aislamiento por usuario no vive en un atributo sino en el predicado
/// SQL de cada handler, y un recordatorio ajeno responde <b>404</b>, nunca 403 — un 403 confirmaría
/// que ese id existe.
/// </para>
/// </summary>
[ApiController]
[Route("reminders")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class RemindersController(IMessageBus bus) : ControllerBase
{
    private const int DefaultSize = 20;
    private const int MaxSize = 100;

    /// <summary>Horizonte por defecto de la agenda cuando el cliente no manda rango.</summary>
    private static readonly TimeSpan DefaultUpcomingWindow = TimeSpan.FromDays(7);

    [HttpPost]
    [HasPermission(ReminderPermissions.Write)]
    [RateLimit("reminder.g.create")]
    [ProducesResponseType<ReminderResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateReminderRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReminderResponse>>(
            new CreateReminderCommand(
                tenantId,
                userId,
                request.Title,
                request.Body,
                request.Category,
                request.TargetId,
                request.FireAtUtc,
                request.AnchorAtUtc,
                request.LeadMinutes,
                request.TimeZone,
                request.RequestKey
            ),
            ct
        );

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(ReminderPermissions.Read)]
    [RateLimit("reminder.f.read")]
    [ProducesResponseType<ReminderResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReminderResponse>>(
            new GetReminderByIdQuery(tenantId, userId, id),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("mine")]
    [HasPermission(ReminderPermissions.Read)]
    [RateLimit("reminder.f.read")]
    [ProducesResponseType<PagedResult<ReminderResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(
        [FromQuery] ReminderStatus? status,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<ReminderResponse>>(
            new ListMyRemindersQuery(tenantId, userId, status, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("upcoming")]
    [HasPermission(ReminderPermissions.Read)]
    [RateLimit("reminder.h.upcoming")]
    [ProducesResponseType<PagedResult<ReminderResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upcoming(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var from = fromUtc ?? DateTime.UtcNow;
        var result = await bus.InvokeAsync<PagedResult<ReminderResponse>>(
            new ListUpcomingRemindersQuery(
                tenantId,
                userId,
                from,
                toUtc ?? from.Add(DefaultUpcomingWindow),
                NormalizePage(page),
                NormalizeSize(size)
            ),
            ct
        );
        return Ok(result);
    }

    [HttpPut("{id:guid}/schedule")]
    [HasPermission(ReminderPermissions.Write)]
    [RateLimit("reminder.g.update")]
    [ProducesResponseType<ReminderResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSchedule(
        Guid id,
        UpdateReminderScheduleRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReminderResponse>>(
            new UpdateReminderScheduleCommand(
                tenantId,
                userId,
                id,
                request.FireAtUtc,
                request.AnchorAtUtc,
                request.LeadMinutes
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/subject")]
    [HasPermission(ReminderPermissions.Write)]
    [RateLimit("reminder.g.update")]
    [ProducesResponseType<ReminderResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSubject(Guid id, UpdateReminderSubjectRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReminderResponse>>(
            new UpdateReminderSubjectCommand(tenantId, userId, id, request.Title, request.Body),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/snooze")]
    [HasPermission(ReminderPermissions.Write)]
    [RateLimit("reminder.g.update")]
    [ProducesResponseType<ReminderResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Snooze(Guid id, SnoozeReminderRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReminderResponse>>(
            new SnoozeReminderCommand(tenantId, userId, id, request.Minutes),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/dismiss")]
    [HasPermission(ReminderPermissions.Write)]
    [RateLimit("reminder.g.update")]
    [ProducesResponseType<ReminderResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReminderResponse>>(
            new DismissReminderCommand(tenantId, userId, id),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Cancelar no borra: transiciona a <c>Cancelled</c>. Se expone como DELETE porque para el
    /// usuario es «quitar el recordatorio», pero la fila queda para el historial.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(ReminderPermissions.Write)]
    [RateLimit("reminder.g.delete")]
    [ProducesResponseType<ReminderResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelReminderRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReminderResponse>>(
            new CancelReminderCommand(tenantId, userId, id, request.Reason),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static int NormalizePage(int page) => page <= 0 ? 1 : page;

    private static int NormalizeSize(int size) => size is <= 0 or > MaxSize ? DefaultSize : size;
}
