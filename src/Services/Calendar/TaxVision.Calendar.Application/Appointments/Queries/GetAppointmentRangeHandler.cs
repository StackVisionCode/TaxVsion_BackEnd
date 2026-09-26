using System.Diagnostics;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Appointments.Queries;

// ActorUserId + CanViewAll: visibilidad por asignación (P2) — quien no ve todo solo ve citas sin cliente, las
// que organiza, o las de sus clientes asignados. OrganizerUserId es el filtro de "agenda de X" (my-day), aparte.
public sealed record GetAppointmentRangeQuery(
    Guid TenantId,
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? OrganizerUserId,
    Guid ActorUserId,
    bool CanViewAll
);

/// <summary>
/// La consulta caliente: el frontend la llama en cada cambio de vista y expande el RRULE de cada serie
/// del tenant. Por eso su política de rate limit es de las restrictivas.
/// </summary>
public static class GetAppointmentRangeHandler
{
    public static async Task<Result<IReadOnlyList<OccurrenceResponse>>> Handle(
        GetAppointmentRangeQuery query,
        IAppointmentRepository appointments,
        ICalendarMetrics metrics,
        IOptions<CalendarVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var candidates = await appointments.ListVisibleForRangeAsync(
            query.TenantId,
            query.FromUtc,
            query.ToUtc,
            assignedTo,
            ct
        );
        var results = new List<OccurrenceResponse>();

        // Se mide sólo la expansión, no la consulta: es la parte que crece con el número de series y
        // la que dice cuándo hay que empezar a cachear.
        var clock = Stopwatch.StartNew();
        var series = 0;

        foreach (var appointment in candidates)
        {
            if (appointment.Status == AppointmentStatus.Cancelled)
                continue;

            if (query.OrganizerUserId is { } organizer && appointment.OrganizerUserId != organizer)
                continue;

            if (appointment.IsRecurring)
                series++;

            var expanded = OccurrenceExpander.Expand(appointment, query.FromUtc, query.ToUtc);
            if (expanded.IsFailure)
                return Result.Failure<IReadOnlyList<OccurrenceResponse>>(expanded.Error);

            foreach (var occurrence in expanded.Value)
                results.Add(OccurrenceResponse.From(occurrence));
        }

        metrics.RecordExpansionDuration(clock.Elapsed.TotalMilliseconds, series);

        return Result.Success<IReadOnlyList<OccurrenceResponse>>(results);
    }
}
