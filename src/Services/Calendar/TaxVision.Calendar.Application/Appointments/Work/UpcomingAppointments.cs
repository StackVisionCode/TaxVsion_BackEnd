using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Queries;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Appointments.Work;

/// <summary>
/// Ventana del Work Profile: desde ahora; "esta semana" termina a la medianoche de la oficina dentro de 7 días;
/// "próximas" llega hasta el rango más ancho que ya expande el calendario.
/// </summary>
internal sealed record WorkWindow(DateTime NowUtc, DateTime DueSoonEndUtc, DateTime HorizonUtc)
{
    public static WorkWindow Of(DateTime nowUtc, ResolvedTimeZone officeZone) =>
        new(
            nowUtc,
            LocalDay.StartUtc(WorkDue.DueSoonEnd(CivilDate.Of(nowUtc, officeZone)), officeZone),
            nowUtc + GetAppointmentRangeHandler.MaxRange
        );
}

/// <summary>Una ocurrencia próxima y la cita de la que sale.</summary>
internal sealed record UpcomingOccurrence(Appointment Appointment, Occurrence Occurrence)
{
    public bool Involves(Guid userId) => UpcomingAppointments.Involves(Appointment, userId);
}

/// <summary>Oficina, visibilidad del que mira (null = sin restricción) y personas cuyo trabajo se cuenta.</summary>
internal sealed record UpcomingScope(Guid TenantId, Guid? AssignedTo, IReadOnlyCollection<Guid> People);

/// <summary>
/// Ocurrencias que empiezan en la ventana donde alguna de las personas organiza o asiste. Igual que my-day:
/// visibilidad por asignación del que mira, sin canceladas y con las series expandidas.
/// </summary>
internal static class UpcomingAppointments
{
    public static async Task<Result<List<UpcomingOccurrence>>> ListAsync(
        IAppointmentRepository appointments,
        UpcomingScope scope,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct
    )
    {
        var candidates = await appointments.ListVisibleForRangeAsync(
            scope.TenantId,
            fromUtc,
            toUtc,
            scope.AssignedTo,
            ct
        );
        var upcoming = new List<UpcomingOccurrence>();
        foreach (var appointment in candidates)
        {
            if (appointment.Status == AppointmentStatus.Cancelled || !scope.People.Any(p => Involves(appointment, p)))
                continue;

            var expanded = OccurrenceExpander.Expand(appointment, fromUtc, toUtc);
            if (expanded.IsFailure)
                return Result.Failure<List<UpcomingOccurrence>>(expanded.Error);

            upcoming.AddRange(
                expanded.Value.Where(o => o.StartUtc >= fromUtc).Select(o => new UpcomingOccurrence(appointment, o))
            );
        }

        upcoming.Sort((a, b) => a.Occurrence.StartUtc.CompareTo(b.Occurrence.StartUtc));
        return Result.Success(upcoming);
    }

    public static bool Involves(Appointment appointment, Guid userId) =>
        appointment.OrganizerUserId == userId || appointment.Attendees.Any(a => a.UserId == userId);
}
