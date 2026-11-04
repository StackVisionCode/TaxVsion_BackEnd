using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.Consumers;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: (1) revoca su feed token (secreto por-usuario que expone su
/// calendario), (2) reasigna al sucesor las citas VIGENTES que él organizaba — o las cancela si no hay
/// sucesor — y le pide ya a Reminder el aviso del sucesor, y (3) lo saca como asistente interno de las
/// citas vigentes. Sin esto, una cita cuyo organizador se fue no la puede cancelar ni reprogramar nadie
/// (no hay override de admin). Las citas pasadas y las canceladas no se tocan (procedencia/historial).
/// Idempotente y sin depender del orden frente al retiro en Reminder (que cancela los avisos del retirado).
/// </summary>
public static class CalendarUserOffboardedConsumer
{
    private const string CancelReason = "The organizer is no longer with the office.";

    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        IAppointmentRepository appointments,
        ICalendarFeedTokenRepository feedTokens,
        ICalendarFeedCache feedCache,
        IAvailabilityRepository availability,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<Appointment> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        var now = DateTime.UtcNow;

        // 1. Revocar el feed token (secreto por-usuario). Se recuerda el hash para desalojar la caché
        //    DESPUÉS del SaveChanges (si no, el camino degradado seguiría sirviendo el token muerto).
        var token = await feedTokens.FindActiveForUserAsync(msg.TenantId, msg.UserId, ct);
        var revokedTokenHashHex =
            token is not null && token.Revoke(now).IsSuccess ? Convert.ToHexString(token.TokenHash) : null;

        // 2. Reasignar (al sucesor) o cancelar (sin sucesor) las citas vigentes del que se va.
        var successor = msg.SuccessorUserId is { } candidate && candidate != msg.UserId ? candidate : (Guid?)null;
        var vigentes = await appointments.ListFutureByOrganizerAsync(msg.TenantId, msg.UserId, now, ct);
        var reassigned = new List<Appointment>();
        var cancelled = new List<Appointment>();
        foreach (var appointment in vigentes)
        {
            if (successor is { } newOrganizer)
            {
                if (appointment.ReassignOrganizer(newOrganizer).IsSuccess)
                    reassigned.Add(appointment);
            }
            else if (appointment.Cancel(msg.UserId, canManageAll: true, CancelReason, now).IsSuccess)
                cancelled.Add(appointment);
        }

        // 3. Sacarlo como asistente interno de las citas vigentes (las recién canceladas se saltan solas).
        var removedAsAttendee = 0;
        foreach (
            var appointment in await appointments.ListFutureByStaffAttendeeAsync(msg.TenantId, msg.UserId, now, ct)
        )
        {
            if (appointment.RemoveDepartedStaffAttendee(msg.UserId).IsSuccess)
                removedAsAttendee++;
        }

        // 4. Limpiar su disponibilidad personal (deja de ser agendable): desactiva sus reglas y borra sus bloqueos.
        await CleanUpAvailabilityAsync(msg, availability, ct);

        await unitOfWork.SaveChangesAsync(ct);

        // 5. Desalojar la caché del feed revocado (tras persistir).
        if (revokedTokenHashHex is not null)
            await feedCache.RemoveAsync(revokedTokenHashHex, ct);

        // 6. Avisar de las cancelaciones (a asistentes y a Reminder) — mismo contrato que CancelAppointmentHandler.
        foreach (var appointment in cancelled)
        {
            await bus.PublishAsync(
                new AppointmentCancelledIntegrationEvent
                {
                    TenantId = msg.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    AppointmentId = appointment.Id,
                    Scope = nameof(EditScope.EntireSeries),
                    OriginalStartUtc = null,
                    Reason = CancelReason,
                    Recipients = AppointmentEvents.RecipientsOf(appointment),
                }
            );
            await bus.PublishAsync(
                new ReminderTargetClosedIntegrationEvent
                {
                    TenantId = msg.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    Category = AppointmentReminderRequests.Category,
                    TargetId = OccurrenceTargetId.For(appointment.Id, appointment.Timing.StartUtc ?? now),
                    Reason = CancelReason,
                }
            );
        }

        // 7. Pedir ya el aviso del sucesor (sin esperar al job). Reminder deduplica por destinatario: crea
        //    el del sucesor aunque el del retirado exista, y el del retirado lo cancela su propio retiro.
        var remindersRequested = 0;
        foreach (var appointment in reassigned)
        {
            foreach (var request in AppointmentReminderRequests.For(appointment, now, correlation.CorrelationId))
            {
                await bus.PublishAsync(request);
                remindersRequested++;
            }
        }

        if (vigentes.Count > 0 || removedAsAttendee > 0 || revokedTokenHashHex is not null)
            logger.LogInformation(
                "Offboarded user {UserId}: feed token revoked={Revoked}, {Reassigned} appointment(s) reassigned ({Reminders} reminder(s) requested for the successor), {Cancelled} cancelled, removed as attendee from {Attended} in tenant {TenantId}.",
                msg.UserId,
                revokedTokenHashHex is not null,
                reassigned.Count,
                remindersRequested,
                cancelled.Count,
                removedAsAttendee,
                msg.TenantId
            );
    }

    /// <summary>
    /// Desactiva las reglas de disponibilidad del que se va (deja de ser agendable) y borra sus bloqueos
    /// puntuales (ausencias inertes de alguien que ya no está; aggregate suelto, sin FK ni auditoría).
    /// No hace SaveChanges: persiste con el del Handle (misma transacción).
    /// </summary>
    private static async Task CleanUpAvailabilityAsync(
        UserOffboardedIntegrationEvent msg,
        IAvailabilityRepository availability,
        CancellationToken ct
    )
    {
        foreach (var rule in await availability.ListRulesAsync(msg.TenantId, msg.UserId, ct))
            rule.Deactivate();

        foreach (var block in await availability.ListAllBlocksForUserAsync(msg.TenantId, msg.UserId, ct))
            availability.RemoveBlock(block);
    }
}
