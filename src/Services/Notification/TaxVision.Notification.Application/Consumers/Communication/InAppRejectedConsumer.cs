using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Communication rechazó un aviso in-app: la fila (oficina + evento + kind + InApp + destinatario) pasa a Failed.
/// Sin kind no se toca nada: varias filas del mismo evento podrían ser candidatas.
/// </summary>
public static class InAppRejectedConsumer
{
    public static async Task Handle(
        InAppNotificationRejectedIntegrationEvent evt,
        INotificationLogStatusRepository logs,
        IUnitOfWork unitOfWork,
        INotificationDispatchMetrics metrics,
        ICorrelationContext correlation,
        ILogger<InAppNotificationRejectedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.TenantId == Guid.Empty)
                return;
            if (string.IsNullOrWhiteSpace(evt.Kind))
            {
                logger.LogWarning(
                    "In-app rejection for event {SourceEventId} came without kind; no log updated.",
                    evt.SourceEventId
                );
                return;
            }

            var log = await logs.FindByRelatedEventAsync(
                evt.TenantId,
                evt.SourceEventId,
                evt.Kind,
                NotificationChannel.InApp,
                NotificationRecipient.ForUser(evt.RecipientUserId).Value,
                ct
            );
            if (log is null)
            {
                logger.LogInformation(
                    "In-app rejection for event {SourceEventId} and kind {Kind} has no matching log.",
                    evt.SourceEventId,
                    evt.Kind
                );
                return;
            }

            var reason = string.IsNullOrWhiteSpace(evt.Detail)
                ? $"rejected_by_communication: {evt.Reason}"
                : $"rejected_by_communication: {evt.Reason} ({evt.Detail})";
            // Repetido: la fila ya no está en Sent y se ignora.
            if (log.FailAfterSend(reason).IsFailure)
                return;

            await unitOfWork.SaveChangesAsync(ct);
            metrics.RecordFailed(evt.Kind, "InApp");
        }
    }
}
