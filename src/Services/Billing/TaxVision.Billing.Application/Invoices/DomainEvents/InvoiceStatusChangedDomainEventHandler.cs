using BuildingBlocks.Messaging.BillingIntegrationEvents;
using TaxVision.Billing.Domain.Invoices.Events;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.DomainEvents;

/// <summary>
/// La factura cambió de estado: devuelve <c>billing.invoice.status_changed.v1</c> por el outbox (corre después del commit,
/// desde la cola domain-events). Communication lo relaya por socket a la oficina y Notification puede reaccionar.
/// </summary>
public static class InvoiceStatusChangedDomainEventHandler
{
    public static InvoiceStatusChangedIntegrationEvent Handle(InvoiceStatusChangedDomainEvent changed, Envelope envelope)
    {
        var eventId = Guid.NewGuid();
        return new InvoiceStatusChangedIntegrationEvent
        {
            EventId = eventId,
            TenantId = changed.TenantId,
            CorrelationId = string.IsNullOrWhiteSpace(envelope.CorrelationId) ? eventId.ToString("N") : envelope.CorrelationId,
            OccurredOn = changed.ChangedAtUtc,
            AggregateId = changed.InvoiceId,
            InvoiceId = changed.InvoiceId,
            InvoiceNumber = changed.InvoiceNumber,
            FromStatus = changed.FromStatus?.ToString(),
            ToStatus = changed.ToStatus.ToString(),
            Trigger = changed.Trigger,
            ChangedByUserId = changed.ChangedByUserId,
        };
    }
}
