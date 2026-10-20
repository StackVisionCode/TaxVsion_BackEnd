using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

public static class SaaSPaymentMethodExpiringSoonConsumer
{
    public static async Task Handle(
        SaaSPaymentMethodExpiringSoonIntegrationEvent evt,
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
            var title = "Payment method expiring soon";
            var body = $"{evt.Brand} ending in {evt.Last4} expires {evt.ExpMonth:D2}/{evt.ExpYear}.";

            await BillingNotificationPublisher.RecordAsync(
                dispatcher,
                recipients,
                evt.TenantId,
                evt.EventId,
                correlation.CorrelationId,
                NotificationKinds.PaymentAppPaymentMethodExpiringSoon,
                "paymentapp.saas_payment_method_expiring_soon.v1",
                evt.OccurredOn,
                title,
                body,
                evt.PaymentMethodId.ToString(),
                new Dictionary<string, string>
                {
                    ["paymentMethodId"] = evt.PaymentMethodId.ToString(),
                    ["brand"] = evt.Brand,
                    ["last4"] = evt.Last4,
                    ["expiry"] = $"{evt.ExpMonth:D2}/{evt.ExpYear}",
                },
                $"billing:payment-method-expiring:{evt.PaymentMethodId:N}",
                ct
            );
        }
    }
}
