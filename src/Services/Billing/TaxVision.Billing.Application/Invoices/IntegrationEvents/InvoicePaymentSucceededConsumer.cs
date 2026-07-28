using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.IntegrationEvents;

/// <summary>
/// Fase 3: marca la factura <c>Paid</c> cuando PaymentClient confirma un cobro exitoso. Reacciona a
/// <see cref="TenantPaymentSucceededIntegrationEvent"/> (semántica "pagado"), NO a PaymentLinkUsed
/// ("redimido"). Solo cobros de tipo InvoicePayment. Correlaciona por el id de factura que viaja en
/// <c>ExternalReferenceId</c>, valida monto/moneda contra el total, y es idempotente: no-op si ya está
/// Paid; el inbox durable de Wolverine dedup a nivel de entrega. IntegrationEventTenantMiddleware ya
/// restauró el tenant; GetByIdAsync usa IgnoreQueryFilters + tenantId explícito.
/// </summary>
public static class InvoicePaymentSucceededConsumer
{
    public static async Task Handle(
        TenantPaymentSucceededIntegrationEvent evt,
        IInvoiceRepository invoices,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<Invoice> logger,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.PurposeKind, "InvoicePayment", StringComparison.OrdinalIgnoreCase))
            return; // No es un cobro de factura; no nos concierne.

        if (!Guid.TryParse(evt.ExternalReferenceId, out var invoiceId))
        {
            logger.LogWarning(
                "TenantPaymentSucceeded with non-invoice ExternalReferenceId '{Ref}'; ignoring.",
                evt.ExternalReferenceId
            );
            return;
        }

        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var invoice = await invoices.GetByIdAsync(evt.TenantId, invoiceId, ct);
            if (invoice is null)
            {
                logger.LogWarning(
                    "TenantPaymentSucceeded for unknown invoice {InvoiceId} in tenant {TenantId}; ignoring.",
                    invoiceId,
                    evt.TenantId
                );
                return;
            }

            if (invoice.Status == InvoiceStatus.Paid)
                return; // Idempotente: ya pagada (reproceso del mismo o un evento posterior).

            var result = invoice.MarkPaid(evt.AmountCents, evt.Currency, evt.PaidAtUtc, PaymentMethod.Online);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not mark invoice {InvoiceId} paid: {Code} - {Message}",
                    invoiceId,
                    result.Error.Code,
                    result.Error.Message
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);

            // Pagada → regenerar el PDF con estado Paid (marca de agua "Pagado" + recibo).
            if (invoice.Status == InvoiceStatus.Paid)
            {
                bus.TenantId = evt.TenantId.ToString();
                await bus.PublishAsync(new GenerateInvoicePdfCommand(evt.TenantId, invoiceId));
            }

            logger.LogInformation(
                "Invoice {InvoiceId} marked {Status} from payment {PaymentId} ({Amount} {Currency}).",
                invoice.Id,
                invoice.Status,
                evt.TenantPaymentId,
                evt.AmountCents,
                evt.Currency
            );
        }
    }
}
