using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

public static class SubscriptionLifecycleInAppConsumer
{
    public static async Task Handle(
        TenantSubscriptionEmailRequestedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var recipients = await BillingNotificationRecipients.ResolveOwnerAndSubscribersAsync(
                evt.TenantId,
                resolver,
                ct
            );
            var renewal = string.Equals(evt.Reason, "RenewalUpcoming", StringComparison.OrdinalIgnoreCase);
            var kind = renewal
                ? NotificationKinds.SubscriptionRenewalUpcoming
                : NotificationKinds.SubscriptionStatusChanged;
            var title = renewal ? "Subscription renewal coming up" : SubscriptionTitle(evt.Status, evt.Reason);

            await BillingNotificationPublisher.RecordAsync(
                dispatcher,
                recipients,
                evt.TenantId,
                evt.EventId,
                correlation.CorrelationId,
                kind,
                "auth.tenant_subscription_email_requested.v1",
                evt.OccurredOn,
                title,
                SubscriptionBody(evt),
                evt.TenantId.ToString(),
                new Dictionary<string, string>
                {
                    ["status"] = evt.Status,
                    ["reason"] = evt.Reason,
                    ["daysUntilRenewal"] = evt.DaysUntilRenewal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                },
                // El GroupKey incluye el reason para que un "RenewalUpcoming" posterior no
                // reagrupe sobre un "RecurringChargeFailed" sin leer: cada motivo mantiene su
                // propia fila en la bandeja.
                $"billing:subscription:{evt.TenantId:N}:{BillingGroupToken(evt.Reason)}",
                ct
            );
        }
    }

    private static string BillingGroupToken(string reason) =>
        string.IsNullOrWhiteSpace(reason) ? "status" : reason.Trim().ToLowerInvariant();

    private static string SubscriptionTitle(string status, string reason)
    {
        if (string.Equals(reason, "CancellationScheduled", StringComparison.OrdinalIgnoreCase))
            return "Subscription cancellation scheduled";
        if (string.Equals(reason, "AccessEnding", StringComparison.OrdinalIgnoreCase))
            return "Subscription access ending soon";
        if (string.Equals(reason, "RecurringChargeFailed", StringComparison.OrdinalIgnoreCase))
            return "Subscription payment failed";
        if (string.Equals(reason, "RecurringChargeSucceeded", StringComparison.OrdinalIgnoreCase))
            return "Subscription payment received";
        return $"Subscription {status.ToLowerInvariant()}";
    }

    private static string SubscriptionBody(TenantSubscriptionEmailRequestedIntegrationEvent evt) =>
        evt.DaysUntilRenewal is { } days
            ? $"Your subscription renews in {days} day{(days == 1 ? string.Empty : "s")}."
            : "Review your billing settings for details.";
}
