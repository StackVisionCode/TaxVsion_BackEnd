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
/// calendario) y (2) reasigna al sucesor las citas VIGENTES que él organizaba — o las cancela si no hay
/// sucesor. Sin esto, una cita cuyo organizador se fue no la puede cancelar ni reprogramar nadie (no hay
/// override de admin). Las citas pasadas y las canceladas no se tocan (procedencia/historial). Idempotente.
/// </summary>
public static class CalendarUserOffboardedConsumer
{
    private const string CancelReason = "The organizer was removed from the tenant.";

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
        var cancelled = new List<Appointment>();
        foreach (var appointment in vigentes)
        {
            if (successor is { } newOrganizer)
                appointment.ReassignOrganizer(newOrganizer);
            else if (appointment.Cancel(msg.UserId, CancelReason, now).IsSuccess)
                cancelled.Add(appointment);
        }

        // 3. Limpiar su disponibilidad personal (deja de ser agendable): desactiva sus reglas y borra sus bloqueos.
        await CleanUpAvailabilityAsync(msg, availability, ct);

        await unitOfWork.SaveChangesAsync(ct);

        // 4. Desalojar la caché del feed revocado (tras persistir).
        if (revokedTokenHashHex is not null)
            await feedCache.RemoveAsync(revokedTokenHashHex, ct);

        // 5. Avisar de las cancelaciones (a asistentes y a Reminder) — mismo contrato que CancelAppointmentHandler.
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
                    Category = "Calendar",
                    TargetId = OccurrenceTargetId.For(appointment.Id, appointment.Timing.StartUtc ?? now),
                    Reason = CancelReason,
                }
            );
        }

        if (vigentes.Count > 0 || revokedTokenHashHex is not null)
            logger.LogInformation(
                "Offboarded user {UserId}: feed token revoked={Revoked}, {Reassigned} appointment(s) reassigned, {Cancelled} cancelled in tenant {TenantId}.",
                msg.UserId,
                revokedTokenHashHex is not null,
                successor is null ? 0 : vigentes.Count,
                cancelled.Count,
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
