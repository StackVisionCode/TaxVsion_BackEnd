using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>
/// Traduce un cambio de entrega al evento que entiende Notification. Solo para correos que pidió
/// Notification (tienen log e intento); los de Correspondence no tienen a quién avisar.
/// </summary>
internal static class DeliveryFeedbackNotifier
{
    public static IntegrationEvent? EventFor(SentMessage message, DeliveryFeedback feedback, string correlationId)
    {
        if (message.NotificationLogId is not { } logId || message.DispatchAttemptId is not { } attemptId)
            return null;

        var at = feedback.OccurredAtUtc;
        if (feedback.Kind == DeliveryFeedbackKind.Signal)
            return new PostmasterEmailEngagementIntegrationEvent
            {
                TenantId = message.TenantId,
                CorrelationId = correlationId,
                NotificationLogId = logId,
                DispatchAttemptId = attemptId,
                SentMessageId = message.Id,
                Signal = feedback.Signal!.Value.ToString(),
                CampaignId = message.CampaignId,
                EventAtUtc = at,
            };

        return feedback.Outcome switch
        {
            DeliveryOutcome.Delivered => new PostmasterEmailDeliveredIntegrationEvent
            {
                TenantId = message.TenantId,
                CorrelationId = correlationId,
                NotificationLogId = logId,
                DispatchAttemptId = attemptId,
                SentMessageId = message.Id,
                CampaignId = message.CampaignId,
                EventAtUtc = at,
            },
            DeliveryOutcome.Complained => new PostmasterEmailComplainedIntegrationEvent
            {
                TenantId = message.TenantId,
                CorrelationId = correlationId,
                NotificationLogId = logId,
                DispatchAttemptId = attemptId,
                SentMessageId = message.Id,
                CampaignId = message.CampaignId,
                EventAtUtc = at,
            },
            DeliveryOutcome.HardBounced or DeliveryOutcome.SoftBounced or DeliveryOutcome.Rejected =>
                new PostmasterEmailDeliveryBouncedIntegrationEvent
                {
                    TenantId = message.TenantId,
                    CorrelationId = correlationId,
                    NotificationLogId = logId,
                    DispatchAttemptId = attemptId,
                    SentMessageId = message.Id,
                    ProviderMessageId = null,
                    BounceType = BounceTypeOf(feedback.Outcome.Value),
                    Reason = feedback.Diagnostic ?? feedback.Outcome.Value.ToString(),
                    CampaignId = message.CampaignId,
                    EventAtUtc = at,
                },
            // Deferred es transitorio: no se avisa.
            _ => null,
        };
    }

    private static string BounceTypeOf(DeliveryOutcome outcome) =>
        outcome switch
        {
            DeliveryOutcome.HardBounced => "Hard",
            DeliveryOutcome.SoftBounced => "Soft",
            _ => "Rejected",
        };
}
