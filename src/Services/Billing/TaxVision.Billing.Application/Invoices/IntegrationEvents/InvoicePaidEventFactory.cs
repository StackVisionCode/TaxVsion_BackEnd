using BuildingBlocks.Messaging.BillingIntegrationEvents;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Application.Invoices.IntegrationEvents;

public static class InvoicePaidEventFactory
{
    public static InvoicePaidIntegrationEvent Create(Invoice invoice, string correlationId)
    {
        if (invoice.Status != InvoiceStatus.Paid)
            throw new InvalidOperationException("Invoice must be paid before publishing the event.");

        var eventId = Guid.NewGuid();

        return new InvoicePaidIntegrationEvent
        {
            EventId = eventId,
            TenantId = invoice.TenantId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId,
            OccurredOn = invoice.PaidAtUtc ?? DateTime.UtcNow,
            AggregateId = invoice.Id,
            InvoiceId = invoice.Id,
            InvoiceNumber =
                invoice.InvoiceNumber ?? throw new InvalidOperationException("Paid invoice must have a number."),
            CustomerId = invoice.CustomerId,
            AmountPaidCents = invoice.AmountPaid.AmountCents,
            Currency = invoice.Currency,
            PaymentMethod = invoice.PaymentMethod?.ToString() ?? PaymentMethod.Other.ToString(),
            PaidAtUtc =
                invoice.PaidAtUtc ?? throw new InvalidOperationException("Paid invoice must have a payment date."),
            ReceiptId = null,
            PaidPdfFileId = invoice.PaidPdfFileId,
        };
    }
}
