using BuildingBlocks.Messaging.BillingIntegrationEvents;
using TaxVision.Billing.Domain.Invoices.Events;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.DomainEvents;

/// <summary>La factura se emitió: devuelve InvoiceIssued (outbox). Corre después del commit, desde la cola domain-events.</summary>
public static class InvoiceIssuedDomainEventHandler
{
    public static InvoiceIssuedIntegrationEvent Handle(InvoiceIssuedDomainEvent issued, Envelope envelope)
    {
        var eventId = Guid.NewGuid();
        return new InvoiceIssuedIntegrationEvent
        {
            EventId = eventId,
            TenantId = issued.TenantId,
            CorrelationId = string.IsNullOrWhiteSpace(envelope.CorrelationId)
                ? eventId.ToString("N")
                : envelope.CorrelationId,
            CausationId = envelope.Id.ToString(),
            OccurredOn = issued.IssuedAtUtc,
            AggregateId = issued.InvoiceId,
            InvoiceId = issued.InvoiceId,
            InvoiceNumber = issued.InvoiceNumber,
            CustomerId = issued.CustomerId,
            TotalAmountCents = issued.TotalAmountCents,
            Currency = issued.Currency,
            DueDateUtc = issued.DueDateUtc,
            IssuedByUserId = issued.IssuedByUserId,
        };
    }
}
