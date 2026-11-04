using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Appointments;

/// <summary>
/// Arma los pedidos a Reminder de una cita: uno por ocurrencia del horizonte, para el organizador
/// VIGENTE. Lo comparten el job periódico y el retiro (que pide en el acto para el sucesor, sin
/// esperar al próximo tick del job).
///
/// <para>
/// Reminder deduplica por (tenant, destinatario, <c>RequestKey</c>): republicar es gratis, y al
/// cambiar el organizador el mismo pedido crea el aviso del nuevo sin tocar el del anterior.
/// </para>
/// </summary>
public static class AppointmentReminderRequests
{
    /// <summary>Categoría con la que Reminder identifica a Calendar.</summary>
    public const string Category = "Calendar";

    /// <summary>Días hacia adelante que se piden por pasada.</summary>
    public const int HorizonDays = 60;

    /// <summary>Clave estable por ocurrencia. No cambiarla: huerfanizaría los recordatorios vivos.</summary>
    public static string RequestKey(Guid occurrenceTargetId) => $"calendar:{occurrenceTargetId:N}";

    /// <summary>Vacío si la cita no pidió aviso, está cancelada o no se puede expandir.</summary>
    public static IReadOnlyList<ReminderRequestedIntegrationEvent> For(
        Appointment appointment,
        DateTime nowUtc,
        string correlationId
    )
    {
        if (appointment.ReminderLeadMinutes is null || appointment.Status == AppointmentStatus.Cancelled)
            return [];

        var occurrences = OccurrenceExpander.Expand(appointment, nowUtc, nowUtc.AddDays(HorizonDays));
        if (occurrences.IsFailure)
            return [];

        var requests = new List<ReminderRequestedIntegrationEvent>(occurrences.Value.Count);
        foreach (var occurrence in occurrences.Value)
        {
            var targetId = OccurrenceTargetId.For(appointment.Id, occurrence.OriginalStartUtc);
            requests.Add(
                new ReminderRequestedIntegrationEvent
                {
                    TenantId = appointment.TenantId,
                    CorrelationId = correlationId,
                    UserId = appointment.OrganizerUserId,
                    Category = Category,
                    TargetId = targetId,
                    Title = occurrence.Title,
                    TimeZoneId = appointment.Timing.TimeZone.Id,
                    AnchorAtUtc = occurrence.StartUtc,
                    LeadMinutes = appointment.ReminderLeadMinutes,
                    RequestKey = RequestKey(targetId),
                }
            );
        }

        return requests;
    }
}
