using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

public static class PayoutFailedNotificationConsumer
{
    public static async Task Handle(
        PayoutFailedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        PushLinks links,
        IRecipientLocale locales,
        IUserEmailResolver emails,
        IScribeRenderClient scribe,
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
            if (recipients.Count == 0)
                return;

            // Correo obligatorio antes de la campana.
            var variables = new Dictionary<string, object?>
            {
                ["amount_label"] = FormatAmount(evt.AmountCents, evt.Currency),
                ["failed_at"] = ScribeDates.Instant(evt.FailedAtUtc),
                ["action_url"] = await OfficeAlertEmails.BillingUrlAsync(links, evt.TenantId, ct),
            };
            if (!string.IsNullOrWhiteSpace(evt.FailureReason))
                variables["failure_reason"] = evt.FailureReason;
            if (!string.IsNullOrWhiteSpace(evt.ProviderPayoutReference))
                variables["payout_reference"] = evt.ProviderPayoutReference;
            await OfficeAlertEmails.SendAsync(
                new NotificationIntent(
                    NotificationKinds.PaymentClientPayoutFailed,
                    evt.TenantId,
                    new NotificationTarget(),
                    evt.EventId,
                    "paymentclient.payout_failed.v1",
                    evt.FailedAtUtc,
                    correlation.CorrelationId
                ),
                "payments.payout_failed.v1",
                variables,
                recipients,
                dispatcher,
                emails,
                scribe,
                locales,
                ct
            );

            await BillingNotificationPublisher.RecordAsync(
                dispatcher,
                recipients,
                evt.TenantId,
                evt.EventId,
                correlation.CorrelationId,
                NotificationKinds.PaymentClientPayoutFailed,
                "paymentclient.payout_failed.v1",
                evt.FailedAtUtc,
                "Payout failed",
                "Review the payout details and retry when the issue is resolved.",
                evt.PayoutScheduleId.ToString(),
                new Dictionary<string, string>
                {
                    ["payoutScheduleId"] = evt.PayoutScheduleId.ToString(),
                    ["amount"] = FormatAmount(evt.AmountCents, evt.Currency),
                    ["currency"] = evt.Currency,
                },
                $"billing:payout-failed:{evt.PayoutScheduleId:N}",
                ct
            );
        }
    }

    private static string FormatAmount(long amountCents, string currency) =>
        $"{amountCents / 100m:0.00} {currency.ToUpperInvariant()}";
}
