using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Application.Invoices.IntegrationEvents;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Invoices.Events;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.DomainEvents;

/// <summary>
/// La factura quedó pagada: devuelve InvoicePaid (outbox) y, si la pagó un cobro, el PDF con la marca "Pagado"
/// (cola local durable). Corre después del commit, desde la cola domain-events; todo sale del evento.
/// </summary>
public static class InvoicePaidDomainEventHandler
{
    public static OutgoingMessages Handle(InvoicePaidDomainEvent paid, Envelope envelope)
    {
        var messages = new OutgoingMessages { InvoicePaidEventFactory.Create(paid, envelope.CorrelationId) };

        // En la emisión con crédito y en la edición, el PDF ya lo encadena EnsureInvoicePaymentLink.
        if (paid.PaymentSource is InvoicePaymentSource.Manual or InvoicePaymentSource.Online)
            messages.Add(new GenerateInvoicePdfCommand(paid.TenantId, paid.InvoiceId));

        return messages;
    }
}
