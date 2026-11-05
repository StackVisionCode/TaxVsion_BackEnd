using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Appointments;

/// <summary>
/// Qué objetivos tiene vivos una cita en Reminder y cómo dejarlos al día después de cambiarla.
/// La fórmula de <see cref="OccurrenceTargetId"/> no cambia: se cierra el objetivo viejo y se pide
/// el nuevo, así que el orden de llegada a Reminder no importa.
/// </summary>
public static class AppointmentReminderTargets
{
    /// <summary>Un id por ocurrencia del horizonte, el mismo que usan los pedidos.</summary>
    public static IReadOnlySet<Guid> Snapshot(Appointment appointment, DateTime nowUtc)
    {
        var occurrences = OccurrenceExpander.Expand(
            appointment,
            nowUtc,
            nowUtc.AddDays(AppointmentReminderRequests.HorizonDays)
        );
        if (occurrences.IsFailure)
            return new HashSet<Guid>();

        return occurrences.Value.Select(o => OccurrenceTargetId.For(appointment.Id, o.OriginalStartUtc)).ToHashSet();
    }

    public static IEnumerable<ReminderTargetClosedIntegrationEvent> Close(
        Appointment appointment,
        IEnumerable<Guid> targets,
        string correlationId,
        string? reason
    ) =>
        targets.Select(target => new ReminderTargetClosedIntegrationEvent
        {
            TenantId = appointment.TenantId,
            CorrelationId = correlationId,
            Category = AppointmentReminderRequests.Category,
            TargetId = target,
            Reason = reason,
        });

    /// <summary>
    /// Después de mover la cita o cambiar su aviso: cierra lo que ya no está y pide lo nuevo.
    /// La generación sube solo si ningún aviso pedido sobrevive; si alguno sobrevive, subirla haría
    /// que el job lo pidiera de nuevo con otra clave (aviso doble).
    /// </summary>
    public static IReadOnlyList<IntegrationEvent> AfterChange(
        Appointment appointment,
        IReadOnlySet<Guid> before,
        int? previousLeadMinutes,
        DateTime nowUtc,
        string correlationId
    )
    {
        var after = Snapshot(appointment, nowUtc);
        var hadReminders = previousLeadMinutes is not null;
        var wantsReminders = appointment.ReminderLeadMinutes is not null;

        HashSet<Guid> survivors = hadReminders && wantsReminders ? before.Where(after.Contains).ToHashSet() : [];
        IEnumerable<Guid> closed =
            !hadReminders ? []
            : wantsReminders ? before.Where(t => !after.Contains(t))
            : before;

        if (survivors.Count == 0)
            appointment.StartNewReminderGeneration();

        var messages = new List<IntegrationEvent>(Close(appointment, closed, correlationId, "rescheduled"));
        messages.AddRange(
            AppointmentReminderRequests.For(appointment, nowUtc, correlationId, t => !survivors.Contains(t))
        );

        // CAL-7: los que conservan su hora reciben los minutos nuevos sobre el mismo objetivo.
        if (survivors.Count > 0 && previousLeadMinutes != appointment.ReminderLeadMinutes)
            messages.AddRange(LeadChanged(appointment, survivors, nowUtc, correlationId));

        return messages;
    }

    private static IEnumerable<ReminderTargetMovedIntegrationEvent> LeadChanged(
        Appointment appointment,
        HashSet<Guid> survivors,
        DateTime nowUtc,
        string correlationId
    ) =>
        AppointmentReminderRequests
            .For(appointment, nowUtc, correlationId, survivors.Contains)
            .Select(request => new ReminderTargetMovedIntegrationEvent
            {
                TenantId = appointment.TenantId,
                CorrelationId = correlationId,
                Category = AppointmentReminderRequests.Category,
                TargetId = request.TargetId!.Value,
                NewAnchorAtUtc = request.AnchorAtUtc!.Value,
                LeadMinutes = request.LeadMinutes,
            });
}
