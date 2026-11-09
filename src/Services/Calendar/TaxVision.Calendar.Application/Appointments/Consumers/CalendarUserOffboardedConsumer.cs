using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Domain.Appointments;

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
        ILogger<Appointment> logger,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;

        // 1. Revocar TODOS sus feed tokens activos. Se recuerdan los hashes para desalojar la caché
        //    DESPUÉS del SaveChanges (si no, el camino degradado seguiría sirviendo el token muerto).
        var revokedTokenHashes = new List<string>();
        foreach (var token in await feedTokens.ListActiveForUserAsync(msg.TenantId, msg.UserId, ct))
        {
            if (token.Revoke(now).IsSuccess)
                revokedTokenHashes.Add(Convert.ToHexString(token.TokenHash));
        }

        // 2. Reasignar (al sucesor) o cancelar (sin sucesor) las citas vigentes del que se va.
        var successor = msg.SuccessorUserId is { } candidate && candidate != msg.UserId ? candidate : (Guid?)null;
        var vigentes = await appointments.ListFutureByOrganizerAsync(msg.TenantId, msg.UserId, now, ct);
        var reassigned = 0;
        var cancelled = 0;
        foreach (var appointment in vigentes)
        {
            if (successor is { } newOrganizer)
            {
                if (appointment.ReassignOrganizer(newOrganizer).IsSuccess)
                {
                    // El aviso del sucesor se pide ya, sin esperar al job. Reminder deduplica por destinatario:
                    // crea el del sucesor aunque el del retirado exista, y ese lo cancela su propio retiro.
                    appointment.RefreshReminders([], leadChanged: false, now);
                    reassigned++;
                }
                continue;
            }

            // Los objetivos se calculan antes de cancelar: después la cita ya no los produce.
            var targets = appointment.ReminderLeadMinutes is null
                ? new HashSet<Guid>()
                : AppointmentReminderTargets.Snapshot(appointment, now);
            if (appointment.Cancel(msg.UserId, canManageAll: true, CancelReason, now).IsSuccess)
            {
                appointment.CloseReminderTargets([.. targets], CancelReason, now);
                cancelled++;
            }
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

        // 5. Desalojar la caché de los feeds revocados (tras persistir).
        foreach (var hash in revokedTokenHashes)
            await feedCache.RemoveAsync(hash, ct);

        if (vigentes.Count > 0 || removedAsAttendee > 0 || revokedTokenHashes.Count > 0)
            logger.LogInformation(
                "Offboarded user {UserId}: {Revoked} feed token(s) revoked, {Reassigned} appointment(s) reassigned, {Cancelled} cancelled, removed as attendee from {Attended} in tenant {TenantId}.",
                msg.UserId,
                revokedTokenHashes.Count,
                reassigned,
                cancelled,
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
