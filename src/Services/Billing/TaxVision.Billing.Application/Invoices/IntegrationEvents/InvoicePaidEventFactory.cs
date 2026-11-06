using BuildingBlocks.Messaging.BillingIntegrationEvents;
using TaxVision.Billing.Domain.Invoices.Events;

namespace TaxVision.Billing.Application.Invoices.IntegrationEvents;

/// <summary>Único traductor de InvoicePaidDomainEvent al contrato de integración.</summary>
public static class InvoicePaidEventFactory
{
    public static InvoicePaidIntegrationEvent Create(InvoicePaidDomainEvent paid, string? correlationId)
    {
        var eventId = Guid.NewGuid();
        var paidAtUtc = paid.PaidAtUtc ?? throw new InvalidOperationException("Paid invoice must have a payment date.");

        return new InvoicePaidIntegrationEvent
        {
            EventId = eventId,
            TenantId = paid.TenantId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId,
            OccurredOn = paidAtUtc,
            AggregateId = paid.InvoiceId,
            InvoiceId = paid.InvoiceId,
            InvoiceNumber = paid.InvoiceNumber ?? throw new InvalidOperationException("Paid invoice must have a number."),
            CustomerId = paid.CustomerId,
            AmountPaidCents = paid.AmountPaidCents,
            Currency = paid.Currency,
            PaymentMethod = paid.PaymentMethod.ToString(),
            PaidAtUtc = paidAtUtc,
            ReceiptId = null,
            PaidPdfFileId = paid.PaidPdfFileId,
        };
    }
}
