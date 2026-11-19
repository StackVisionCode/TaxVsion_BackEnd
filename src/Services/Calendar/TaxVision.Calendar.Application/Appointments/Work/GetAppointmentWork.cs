using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;

namespace TaxVision.Calendar.Application.Appointments.Work;

/// <summary>
/// Próximas citas de <paramref name="UserId"/> (Work Profile) vistas por el actor. <c>CanViewAll</c> =
/// customers.view_all del actor; la zona de la oficina define "esta semana". La autorización la decide el controller.
/// </summary>
public sealed record GetAppointmentWorkQuery(
    Guid TenantId,
    Guid UserId,
    Guid ActorUserId,
    bool CanViewAll,
    ResolvedTimeZone OfficeZone,
    DateTime NowUtc
);

/// <summary>Conteos de los próximos 7 días de la oficina por persona, para la tabla de miembros.</summary>
public sealed record GetAppointmentWorkBatchQuery(
    Guid TenantId,
    IReadOnlyList<Guid> UserIds,
    Guid ActorUserId,
    bool CanViewAll,
    ResolvedTimeZone OfficeZone,
    DateTime NowUtc
);

public static class GetAppointmentWorkHandler
{
    public const string Provider = "calendar";

    public static async Task<Result<WorkSummaryResponse>> Handle(
        GetAppointmentWorkQuery query,
        IAppointmentRepository appointments,
        IOptions<CalendarVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var window = WorkWindow.Of(query.NowUtc, query.OfficeZone);
        var scope = new UpcomingScope(
            query.TenantId,
            AssignedTo(visibility.Value, query.ActorUserId, query.CanViewAll),
            [query.UserId]
        );
        var upcoming = await UpcomingAppointments.ListAsync(appointments, scope, window.NowUtc, window.HorizonUtc, ct);
        if (upcoming.IsFailure)
            return Result.Failure<WorkSummaryResponse>(upcoming.Error);

        var dueSoon = upcoming.Value.Count(u => u.Occurrence.StartUtc < window.DueSoonEndUtc);
        var count = new WorkCount(WorkKinds.Appointments, upcoming.Value.Count, 0, dueSoon, 0);
        // Una serie aparece una vez, con su próxima ocurrencia.
        var top = upcoming
            .Value.DistinctBy(u => u.Appointment.Id)
            .Take(WorkLimits.TopItems)
            .Select(u => ToItem(u, query.OfficeZone))
            .ToList();
        return Result.Success(new WorkSummaryResponse(Provider, Provider, [count], top));
    }

    /// <summary>Visibilidad por asignación del que mira, igual que el rango del calendario.</summary>
    internal static Guid? AssignedTo(CalendarVisibilityOptions visibility, Guid actorUserId, bool canViewAll) =>
        visibility.Enabled && !canViewAll ? actorUserId : null;

    // El enlace abre el calendario del CRM en el día de la oficina, con la cita seleccionada.
    private static WorkItem ToItem(UpcomingOccurrence upcoming, ResolvedTimeZone officeZone)
    {
        var start = upcoming.Occurrence.StartUtc;
        var day = CivilDate
            .Of(start, officeZone)
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var id = upcoming.Appointment.Id;
        return new WorkItem(
            id,
            WorkKinds.Appointments,
            upcoming.Occurrence.Title,
            upcoming.Appointment.CustomerId,
            start,
            null,
            upcoming.Appointment.Status.ToString(),
            $"/calendar?date={day}&appointmentId={id}"
        );
    }
}

public static class GetAppointmentWorkBatchHandler
{
    public static async Task<Result<WorkBatchResponse>> Handle(
        GetAppointmentWorkBatchQuery query,
        IAppointmentRepository appointments,
        IOptions<CalendarVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var window = WorkWindow.Of(query.NowUtc, query.OfficeZone);
        var assignedTo = GetAppointmentWorkHandler.AssignedTo(visibility.Value, query.ActorUserId, query.CanViewAll);
        var scope = new UpcomingScope(query.TenantId, assignedTo, query.UserIds);
        var upcoming = await UpcomingAppointments.ListAsync(
            appointments,
            scope,
            window.NowUtc,
            window.DueSoonEndUtc,
            ct
        );
        if (upcoming.IsFailure)
            return Result.Failure<WorkBatchResponse>(upcoming.Error);

        var rows = query
            .UserIds.Select(userId => new WorkBatchRow(userId, upcoming.Value.Count(u => u.Involves(userId)), 0))
            .ToList();
        return Result.Success(new WorkBatchResponse(rows));
    }
}
