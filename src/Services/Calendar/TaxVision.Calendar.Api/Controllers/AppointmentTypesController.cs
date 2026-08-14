using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Calendar.Api.Requests;
using TaxVision.Calendar.Application.Types;
using TaxVision.Calendar.Application.Types.Commands;
using TaxVision.Calendar.Application.Types.Queries;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

/// <summary>
/// El catálogo de tipos de la firma. Definirlo es configuración, así que va con
/// <c>calendar.types.manage</c> y no con el permiso de escribir citas.
/// </summary>
[ApiController]
[Route("calendar/types")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class AppointmentTypesController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.f.read")]
    public async Task<IActionResult> List([FromQuery] bool onlyActive = true, CancellationToken ct = default)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<AppointmentTypeResponse>>>(
            new ListAppointmentTypesQuery(tenantId, onlyActive),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPost]
    [HasPermission(CalendarPermissions.TypesManage)]
    [RateLimit("calendar.g.create")]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentTypeRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<AppointmentTypeResponse>>(
            new CreateAppointmentTypeCommand(
                tenantId,
                request.Name,
                request.DefaultDuration,
                request.ColorHex,
                request.IsVirtual,
                request.BlocksOnConflict,
                request.DailyCap
            ),
            ct
        );

        return result.IsFailure
            ? StatusCode(result.Error.ToHttpStatusCode(), result.Error)
            : CreatedAtAction(nameof(List), new { }, result.Value);
    }

    /// <summary>Siembra los cuatro tipos de arranque. No hace nada si el tenant ya tiene alguno.</summary>
    [HttpPost("install-standard")]
    [HasPermission(CalendarPermissions.TypesManage)]
    [RateLimit("calendar.g.create")]
    public async Task<IActionResult> InstallStandard(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<AppointmentTypeResponse>>>(
            new InstallStandardTypesCommand(tenantId),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }
}
