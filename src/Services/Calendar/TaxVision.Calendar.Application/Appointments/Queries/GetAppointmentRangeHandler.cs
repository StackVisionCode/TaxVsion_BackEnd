using System.Diagnostics;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Appointments.Queries;

// ActorUserId + CanViewAll: visibilidad por asignación (P2) — quien no ve todo solo ve citas sin cliente, las
// que organiza, o las de sus clientes asignados. OrganizerUserId filtra la agenda que organiza X;
// ParticipantUserId la que X organiza o a la que asiste (my-day y la vista "Mine" del CRM).
public sealed record GetAppointmentRangeQuery(
    Guid TenantId,
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? OrganizerUserId,
    Guid ActorUserId,
    bool CanViewAll,
    Guid? ParticipantUserId = null
);

/// <summary>
/// La consulta caliente: el frontend la llama en cada cambio de vista y expande el RRULE de cada serie
/// del tenant. Por eso su política de rate limit es de las restrictivas y el rango tiene tope.
/// </summary>
public static class GetAppointmentRangeHandler
{
    // Cubre la vista mensual (6 semanas) con margen; más ancho expande series de sobra.
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(62);

    public static async Task<Result<IReadOnlyList<OccurrenceResponse>>> Handle(
        GetAppointmentRangeQuery query,
        IAppointmentRepository appointments,
        IAppointmentTypeRepository types,
        ICalendarMetrics metrics,
        IOptions<CalendarVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (query.ToUtc <= query.FromUtc || query.ToUtc - query.FromUtc > MaxRange)
            return Result.Failure<IReadOnlyList<OccurrenceResponse>>(AppointmentErrors.RangeInvalid);

        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var candidates = await appointments.ListVisibleForRangeAsync(
            query.TenantId,
            query.FromUtc,
            query.ToUtc,
            assignedTo,
            ct
        );
        // Una sola lectura de tipos por consulta, no una por cita.
        var typesById = (await types.ListAsync(query.TenantId, onlyActive: false, ct)).ToDictionary(t => t.Id);
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

            if (query.ParticipantUserId is { } participant && !Participates(appointment, participant))
                continue;

            if (appointment.IsRecurring)
                series++;

            var expanded = OccurrenceExpander.Expand(appointment, query.FromUtc, query.ToUtc);
            if (expanded.IsFailure)
                return Result.Failure<IReadOnlyList<OccurrenceResponse>>(expanded.Error);

            typesById.TryGetValue(appointment.AppointmentTypeId, out var type);
            foreach (var occurrence in expanded.Value)
                results.Add(OccurrenceResponse.From(occurrence, appointment, type));
        }

        metrics.RecordExpansionDuration(clock.Elapsed.TotalMilliseconds, series);

        results.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
        return Result.Success<IReadOnlyList<OccurrenceResponse>>(results);
    }

    private static bool Participates(Appointment appointment, Guid userId) =>
        appointment.OrganizerUserId == userId || appointment.Attendees.Any(a => a.UserId == userId);
}
