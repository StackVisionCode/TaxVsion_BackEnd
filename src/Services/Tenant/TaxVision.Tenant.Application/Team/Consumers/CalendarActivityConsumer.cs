using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>
/// Citas movidas o canceladas por un miembro (<c>ChangedByUserId</c>). El organizador nunca se toma como actor, y
/// el alta queda fuera: <c>AppointmentScheduled</c> no dice quién la agendó.
/// </summary>
public static class CalendarActivityConsumer
{
    public static Task Handle(
        AppointmentRescheduledIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(
                evt.ChangedByUserId,
                ActivityVerbs.AppointmentRescheduled,
                evt.AppointmentId,
                CustomerOf(evt.Recipients)
            ),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        AppointmentCancelledIntegrationEvent evt,
        IActivityEntryRepository entries,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ActivityRecorder.RecordAsync(
            evt,
            new(evt.ChangedByUserId, ActivityVerbs.AppointmentCancelled, evt.AppointmentId, CustomerOf(evt.Recipients)),
            entries,
            unitOfWork,
            correlation,
            ct
        );

    // El cliente de la cita viaja entre los destinatarios; sirve para el ABAC de quien mira.
    private static Guid? CustomerOf(IReadOnlyList<AppointmentRecipient> recipients)
    {
        foreach (var recipient in recipients)
        {
            if (recipient.CustomerId is { } customerId && customerId != Guid.Empty)
                return customerId;
        }

        return null;
    }
}
