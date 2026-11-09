using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <summary>
/// Comprueba el hueco del organizador antes de guardar.
///
/// <para>
/// Devuelve la lista de avisos cuando el solapamiento no bloquea, y falla con
/// <c>Calendar.Appointment.Conflict</c> cuando sí — que la capa web mapea a 409.
/// </para>
/// </summary>
internal static class ConflictProbe
{
    public static async Task<Result<IReadOnlyList<string>>> CheckAsync(
        Appointment appointment,
        AppointmentType type,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        CancellationToken ct
    )
    {
        var (fromUtc, toUtc) = Window(appointment);

        var occurrences = OccurrenceExpander.Expand(appointment, fromUtc, toUtc);
        if (occurrences.IsFailure)
            return Result.Failure<IReadOnlyList<string>>(occurrences.Error);

        var slots = occurrences.Value.Select(o => (o.StartUtc, o.EndUtc)).ToList();
        return await CheckSlotsAsync(appointment, slots, fromUtc, toUtc, type, appointments, availability, ct);
    }

    /// <summary>Solo esta franja: mover una ocurrencia no responde por el resto de la serie.</summary>
    public static Task<Result<IReadOnlyList<string>>> CheckSlotAsync(
        Appointment appointment,
        DateTime startUtc,
        DateTime endUtc,
        AppointmentType type,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        CancellationToken ct
    ) => CheckSlotsAsync(appointment, [(startUtc, endUtc)], startUtc, endUtc, type, appointments, availability, ct);

    private static async Task<Result<IReadOnlyList<string>>> CheckSlotsAsync(
        Appointment appointment,
        IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> slots,
        DateTime fromUtc,
        DateTime toUtc,
        AppointmentType type,
        IAppointmentRepository appointments,
        IAvailabilityRepository availability,
        CancellationToken ct
    )
    {
        var existing = await appointments.ListForRangeAsync(appointment.TenantId, fromUtc, toUtc, ct);
        var busy = Busy(existing, appointment, fromUtc, toUtc);
        var blocks = await availability.ListBlocksAsync(
            appointment.TenantId,
            appointment.OrganizerUserId,
            fromUtc,
            toUtc,
            ct
        );

        var warnings = new List<string>();
        foreach (var (startUtc, endUtc) in slots)
        {
            var result = ConflictDetector.Check(startUtc, endUtc, type, busy, blocks);
            if (!result.HasConflict)
                continue;

            if (result.Blocks)
                return Result.Failure<IReadOnlyList<string>>(AppointmentErrors.Conflict);

            warnings.Add($"Se solapa con otra cita el {startUtc:yyyy-MM-dd HH:mm} UTC.");
        }

        return Result.Success<IReadOnlyList<string>>(warnings);
    }

    /// <summary>
    /// De una serie se comprueban los próximos 90 días, no las 156 ocurrencias de tres años: el choque
    /// que importa es el de las próximas semanas.
    /// </summary>
    private static (DateTime From, DateTime To) Window(Appointment appointment)
    {
        if (appointment.IsRecurring)
        {
            var from = DateTime.UtcNow;
            return (from, from.AddDays(ConflictDetector.SeriesLookaheadDays));
        }

        var start = appointment.Timing.StartUtc ?? DateTime.UtcNow;
        var end = appointment.Timing.EndUtc ?? start.AddHours(1);
        return (start, end);
    }

    private static List<Occurrence> Busy(
        IReadOnlyList<Appointment> existing,
        Appointment self,
        DateTime fromUtc,
        DateTime toUtc
    )
    {
        var busy = new List<Occurrence>();
        foreach (var other in existing)
        {
            // La serie de la que sale una partida es la misma cita: tampoco choca con ella.
            if (
                other.Id == self.Id
                || other.Id == self.SplitFromSeriesId
                || other.Status == AppointmentStatus.Cancelled
            )
                continue;

            if (other.OrganizerUserId != self.OrganizerUserId)
                continue;

            var expanded = OccurrenceExpander.Expand(other, fromUtc, toUtc);
            if (expanded.IsSuccess)
                busy.AddRange(expanded.Value);
        }

        return busy;
    }
}
