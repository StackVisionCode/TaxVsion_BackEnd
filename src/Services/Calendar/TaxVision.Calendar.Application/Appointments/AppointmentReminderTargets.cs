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
        Guid tenantId,
        IEnumerable<Guid> targets,
        string correlationId,
        string? reason
    ) =>
        targets.Select(target => new ReminderTargetClosedIntegrationEvent
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            Category = AppointmentReminderRequests.Category,
            TargetId = target,
            Reason = reason,
        });

    /// <summary>Lo que se pide a Reminder al refrescar: lo nuevo, y los minutos nuevos a lo que sobrevive.</summary>
    public static IReadOnlyList<IntegrationEvent> Refresh(
        Appointment appointment,
        IReadOnlyCollection<Guid> keptTargets,
        bool leadChanged,
        DateTime nowUtc,
        string correlationId
    )
    {
        var survivors = keptTargets.ToHashSet();
        var messages = new List<IntegrationEvent>(
            AppointmentReminderRequests.For(appointment, nowUtc, correlationId, t => !survivors.Contains(t))
        );

        // CAL-7: los que conservan su hora reciben los minutos nuevos sobre el mismo objetivo.
        if (survivors.Count > 0 && leadChanged)
            messages.AddRange(LeadChanged(appointment, survivors, nowUtc, correlationId));

        return messages;
    }

    /// <summary>
    /// Después de mover la cita o cambiar su aviso: cierra lo que ya no está y pide lo nuevo.
    /// La generación sube solo si ningún aviso pedido sobrevive; si alguno sobrevive, subirla haría
    /// que el job lo pidiera de nuevo con otra clave (aviso doble).
    /// </summary>
    public static void AfterChange(
        Appointment appointment,
        IReadOnlySet<Guid> before,
        int? previousLeadMinutes,
        DateTime nowUtc
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

        appointment.CloseReminderTargets([.. closed], "rescheduled", nowUtc);

        if (wantsReminders)
            appointment.RefreshReminders(
                [.. survivors],
                previousLeadMinutes != appointment.ReminderLeadMinutes,
                nowUtc
            );
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
