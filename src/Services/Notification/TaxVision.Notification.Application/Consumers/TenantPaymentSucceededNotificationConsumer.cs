using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

public static class TenantPaymentSucceededNotificationConsumer
{
    public static async Task Handle(
        TenantPaymentSucceededIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (!string.Equals(evt.PurposeKind, "InvoicePayment", StringComparison.OrdinalIgnoreCase))
                return;

            var recipients = await BillingNotificationRecipients.ResolveSubscribersAsync(evt.TenantId, resolver, ct);
            var resourceId = Guid.TryParse(evt.ExternalReferenceId, out var invoiceId)
                ? invoiceId.ToString()
                : evt.TenantPaymentId.ToString();
            var amount = $"{evt.AmountCents / 100m:0.00} {evt.Currency.ToUpperInvariant()}";

            await BillingNotificationPublisher.RecordAsync(
                dispatcher,
                recipients,
                evt.TenantId,
                evt.EventId,
                correlation.CorrelationId,
                NotificationKinds.PaymentClientTenantPaymentSucceeded,
                "paymentclient.tenant_payment_succeeded.v1",
                evt.PaidAtUtc,
                "Invoice payment received",
                $"A customer payment of {amount} was confirmed.",
                resourceId,
                new Dictionary<string, string>
                {
                    ["tenantPaymentId"] = evt.TenantPaymentId.ToString(),
                    ["amount"] = amount,
                    ["provider"] = evt.ProviderCode,
                },
                $"billing:invoice-payment:{resourceId}",
                ct
            );
        }
    }
}
