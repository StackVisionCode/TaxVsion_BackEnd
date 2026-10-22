using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Consumers.Postmaster;

/// <summary>
/// Consumers de callbacks Postmaster -> Notification. Cada uno resuelve el
/// <see cref="NotificationLog"/> por <c>NotificationLogId</c> y transiciona el
/// <see cref="NotificationDispatchAttempt"/> correspondiente. Dedup por Wolverine
/// <c>UseDurableInbox</c> vía <c>MessageId</c> del evento (ya activo a nivel de servicio).
/// </summary>
internal static class PostmasterCallbackHandling
{
    public const string OrderingPendingCode = "NotificationDispatchAttempt.OrderingPending";
    public const string AlreadyInStatusCode = "NotificationDispatchAttempt.AlreadyInStatus";

    public static string ResolveCorrelation(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}

public static class PostmasterEmailDeliverySucceededConsumer
{
    public static async Task Handle(
        PostmasterEmailDeliverySucceededIntegrationEvent evt,
        INotificationLogQueryRepository logQuery,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        ILogger<PostmasterEmailDeliverySucceededIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(PostmasterCallbackHandling.ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var log = await logQuery.FindWithAttemptsAsync(evt.NotificationLogId, ct);
            if (log is null)
            {
                logger.LogWarning(
                    "Succeeded callback received for unknown NotificationLog {LogId} (attempt {AttemptId}); dropping.",
                    evt.NotificationLogId,
                    evt.DispatchAttemptId
                );
                return;
            }
            var result = log.UpdateAttemptStatus(
                evt.DispatchAttemptId,
                NotificationDispatchAttemptStatus.Sent,
                providerMessageId: evt.ProviderMessageId,
                eventAtUtc: evt.EventAtUtc
            );
            if (result.IsFailure)
            {
                // Succeeded nunca puede estar "pendiente de orden": Sent viene desde Queued, no
                // depende de ningún otro callback previo. Si falla, es idempotente (ya aplicado o terminal).
                logger.LogDebug(
                    "Succeeded callback for log {LogId} attempt {AttemptId} ignored: {Error}",
                    evt.NotificationLogId,
                    evt.DispatchAttemptId,
                    result.Error.Message
                );
                return;
            }
            log.MarkSent();
            await uow.SaveChangesAsync(ct);
        }
    }
}

public static class PostmasterEmailDeliveryFailedConsumer
{
    public static async Task Handle(
        PostmasterEmailDeliveryFailedIntegrationEvent evt,
        INotificationLogQueryRepository logQuery,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        ILogger<PostmasterEmailDeliveryFailedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(PostmasterCallbackHandling.ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var log = await logQuery.FindWithAttemptsAsync(evt.NotificationLogId, ct);
            if (log is null)
            {
                logger.LogWarning(
                    "Failed callback received for unknown NotificationLog {LogId}; dropping.",
                    evt.NotificationLogId
                );
                return;
            }
            var result = log.UpdateAttemptStatus(
                evt.DispatchAttemptId,
                NotificationDispatchAttemptStatus.Failed,
                providerMessageId: evt.ProviderMessageId,
                errorReason: evt.Reason,
                eventAtUtc: evt.EventAtUtc
            );
            if (result.IsFailure)
            {
                logger.LogDebug(
                    "Failed callback for log {LogId} attempt {AttemptId} ignored: {Error}",
                    evt.NotificationLogId,
                    evt.DispatchAttemptId,
                    result.Error.Message
                );
                return;
            }
            log.MarkFailed(evt.Reason);
            await uow.SaveChangesAsync(ct);
        }
    }
}

public static class PostmasterEmailDeliveryBouncedConsumer
{
    public static async Task Handle(
        PostmasterEmailDeliveryBouncedIntegrationEvent evt,
        INotificationLogQueryRepository logQuery,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        ILogger<PostmasterEmailDeliveryBouncedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(PostmasterCallbackHandling.ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var log = await logQuery.FindWithAttemptsAsync(evt.NotificationLogId, ct);
            if (log is null)
            {
                logger.LogWarning("Bounced callback for unknown log {LogId}; dropping.", evt.NotificationLogId);
                return;
            }
            var result = log.UpdateAttemptStatus(
                evt.DispatchAttemptId,
                NotificationDispatchAttemptStatus.Bounced,
                providerMessageId: evt.ProviderMessageId,
                errorReason: $"{evt.BounceType}: {evt.Reason}",
                eventAtUtc: evt.EventAtUtc
            );
            if (result.IsFailure)
            {
                if (result.Error.Code == PostmasterCallbackHandling.OrderingPendingCode)
                {
                    // Bounced llegó antes que Sent; dejar que Wolverine reintente con backoff
                    // en vez de perder el bounce silenciosamente.
                    throw new InvalidOperationException(
                        $"Bounced callback for log {evt.NotificationLogId} attempt {evt.DispatchAttemptId} arrived before Sent; retrying."
                    );
                }
                logger.LogDebug(
                    "Bounced callback for log {LogId} attempt {AttemptId} ignored: {Error}",
                    evt.NotificationLogId,
                    evt.DispatchAttemptId,
                    result.Error.Message
                );
                return;
            }
            log.MarkBounced($"Bounce ({evt.BounceType}): {evt.Reason}");
            await uow.SaveChangesAsync(ct);
        }
    }
}

/// <summary>El proveedor confirmó la entrega: el intento pasa a Delivered y el log también.</summary>
public static class PostmasterEmailDeliveredConsumer
{
    public static async Task Handle(
        PostmasterEmailDeliveredIntegrationEvent evt,
        INotificationLogQueryRepository logQuery,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        ILogger<PostmasterEmailDeliveredIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(PostmasterCallbackHandling.ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var log = await logQuery.FindWithAttemptsAsync(evt.NotificationLogId, ct);
            if (log is null)
                return; // es un OutboundEmailMessage: lo atiende PostmasterOutboundEmailDeliveredConsumer.

            var result = log.UpdateAttemptStatus(
                evt.DispatchAttemptId,
                NotificationDispatchAttemptStatus.Delivered,
                eventAtUtc: evt.EventAtUtc
            );
            if (result.IsFailure)
            {
                // Delivered llegó antes que Sent: se reintenta en vez de perderlo.
                if (result.Error.Code == PostmasterCallbackHandling.OrderingPendingCode)
                    throw new InvalidOperationException(
                        $"Delivered callback for log {evt.NotificationLogId} attempt {evt.DispatchAttemptId} arrived before Sent; retrying."
                    );
                logger.LogDebug(
                    "Delivered callback for log {LogId} attempt {AttemptId} ignored: {Error}",
                    evt.NotificationLogId,
                    evt.DispatchAttemptId,
                    result.Error.Message
                );
                return;
            }
            log.MarkDelivered();
            await uow.SaveChangesAsync(ct);
        }
    }
}

public static class PostmasterEmailDeliverySuppressedConsumer
{
    public static async Task Handle(
        PostmasterEmailDeliverySuppressedIntegrationEvent evt,
        INotificationLogQueryRepository logQuery,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        ILogger<PostmasterEmailDeliverySuppressedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(PostmasterCallbackHandling.ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var log = await logQuery.FindWithAttemptsAsync(evt.NotificationLogId, ct);
            if (log is null)
            {
                logger.LogWarning("Suppressed callback for unknown log {LogId}; dropping.", evt.NotificationLogId);
                return;
            }
            var result = log.UpdateAttemptStatus(
                evt.DispatchAttemptId,
                NotificationDispatchAttemptStatus.Suppressed,
                errorReason: evt.SuppressionReason,
                eventAtUtc: evt.EventAtUtc
            );
            if (result.IsFailure)
            {
                logger.LogDebug(
                    "Suppressed callback for log {LogId} attempt {AttemptId} ignored: {Error}",
                    evt.NotificationLogId,
                    evt.DispatchAttemptId,
                    result.Error.Message
                );
                return;
            }
            log.MarkFailed($"Suppressed: {evt.SuppressionReason}");
            await uow.SaveChangesAsync(ct);
        }
    }
}

public static class PostmasterEmailDeliveryProviderNotConfiguredConsumer
{
    private const string ReasonMessage =
        "Tenant email provider not configured — configure your outbound SMTP in Settings → Email.";

    public static async Task Handle(
        PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent evt,
        INotificationLogQueryRepository logQuery,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        ILogger<PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(PostmasterCallbackHandling.ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var log = await logQuery.FindWithAttemptsAsync(evt.NotificationLogId, ct);
            if (log is null)
            {
                logger.LogWarning(
                    "ProviderNotConfigured callback for unknown log {LogId}; dropping.",
                    evt.NotificationLogId
                );
                return;
            }
            var result = log.UpdateAttemptStatus(
                evt.DispatchAttemptId,
                NotificationDispatchAttemptStatus.ProviderNotConfigured,
                errorReason: ReasonMessage,
                eventAtUtc: evt.EventAtUtc
            );
            if (result.IsFailure)
            {
                logger.LogDebug(
                    "ProviderNotConfigured callback for log {LogId} attempt {AttemptId} ignored: {Error}",
                    evt.NotificationLogId,
                    evt.DispatchAttemptId,
                    result.Error.Message
                );
                return;
            }
            log.MarkFailed(ReasonMessage);
            await uow.SaveChangesAsync(ct);
            logger.LogWarning(
                "Tenant {TenantId} has no email provider configured — email {TemplateKey} deferred (log {LogId}).",
                evt.TenantId,
                log.TemplateKey,
                evt.NotificationLogId
            );
        }
    }
}
