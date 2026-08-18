using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Calendar.Application.Availability.Queries;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

/// <summary>
/// Los huecos libres de una persona. Devuelve intervalos y nada más: quien pregunta por la agenda de
/// un compañero no tiene por qué enterarse de con quién se reúne.
/// </summary>
[ApiController]
[Route("calendar/availability")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class AvailabilityController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.i.availability")]
    public async Task<IActionResult> GetFreeSlots(
        [FromQuery] Guid userId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? typeId,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<FreeSlotResponse>>>(
            new GetAvailabilityQuery(tenantId, userId, AsUtc(from), AsUtc(to), typeId),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>El query string no lleva zona: un valor sin <c>Z</c> se interpreta como UTC.</summary>
    /// <summary>Medido: el binder ya convierte el offset; aca solo llega una fecha sin zona.</summary>
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
