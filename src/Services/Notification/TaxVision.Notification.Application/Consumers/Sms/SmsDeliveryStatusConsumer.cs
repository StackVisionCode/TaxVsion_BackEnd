using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Consumers.Sms;

/// <summary>
/// Resultado real de un SMS propio: el servicio Sms devuelve el <c>SourceContext</c> con el id de la fila de
/// auditoría. Otro contexto (campañas, CRM) se ignora; "aceptado" no cambia nada (la fila ya dice Sent).
/// </summary>
public static class SmsDeliveryStatusConsumer
{
    public static Task Handle(
        SmsMessageDeliveredIntegrationEvent evt,
        INotificationLogStatusRepository logs,
        IUnitOfWork unitOfWork,
        INotificationDispatchMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SmsMessageDeliveredIntegrationEvent> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt,
            evt.SourceContext,
            log => log.ConfirmDelivered(),
            log => metrics.RecordDelivered(log.Kind, "Sms"),
            logs,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        SmsMessageFailedIntegrationEvent evt,
        INotificationLogStatusRepository logs,
        IUnitOfWork unitOfWork,
        INotificationDispatchMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SmsMessageFailedIntegrationEvent> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt,
            evt.SourceContext,
            log => log.FailAfterSend(string.IsNullOrWhiteSpace(evt.FailureCode) ? "failed" : evt.FailureCode),
            log => metrics.RecordFailed(log.Kind, "Sms"),
            logs,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        SmsMessageSuppressedIntegrationEvent evt,
        INotificationLogStatusRepository logs,
        IUnitOfWork unitOfWork,
        INotificationDispatchMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SmsMessageSuppressedIntegrationEvent> logger,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt,
            evt.SourceContext,
            log => log.SkipAfterSend("suppressed"),
            log => metrics.RecordNoRecipient(log.Kind, "Sms"),
            logs,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    private static async Task ApplyAsync(
        BuildingBlocks.Messaging.IntegrationEvent evt,
        string? sourceContext,
        Func<NotificationLog, Result> transition,
        Action<NotificationLog> record,
        INotificationLogStatusRepository logs,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.TenantId == Guid.Empty || !SmsSourceContext.TryGetLogId(sourceContext, out var logId))
                return;

            // Filtra por la oficina del evento: un id ajeno no encuentra fila.
            var log = await logs.FindAsync(evt.TenantId, logId, ct);
            if (log is null || log.Channel != NotificationChannel.Sms)
            {
                logger.LogInformation("SMS result for log {LogId} has no matching row; ignored.", logId);
                return;
            }

            var result = transition(log);
            if (result.IsFailure)
            {
                // Repetido o fuera de orden: el estado actual se queda.
                logger.LogInformation(
                    "SMS result for log {LogId} ignored: status is already {Status}.",
                    logId,
                    log.Status
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            record(log);
        }
    }
}
