using BuildingBlocks.Persistence;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;
using TaxVision.Billing.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;

/// <summary>
/// Asegura (idempotente) el ancla estable de cobro en PaymentClient y guarda su URL en la factura, y
/// SOLO entonces publica <see cref="GenerateInvoicePdfCommand"/>. Reusa el link Active existente en
/// reintentos. Un fallo lanza para que el RetryWithCooldown de Wolverine reintente este paso sin
/// afectar los demás (PaymentClient puede estar caído). El link con token se acuña perezosamente del
/// lado PaymentClient cuando el taxpayer abre la URL estable.
/// </summary>
public static class EnsureInvoicePaymentLinkHandler
{
    public static async Task Handle(
        EnsureInvoicePaymentLinkCommand command,
        IInvoiceRepository invoices,
        IInvoicePaymentLinkClient paymentLinks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (invoice is null || invoice.InvoiceNumber is null)
            return; // Borrador borrado o no emitido; nada que asegurar.

        // El cobro es SIEMPRE por el TOTAL de la factura (decisión del negocio): el checkout cobra el importe
        // completo en un solo pago. Un cambio sobre una factura ya pagada/parcial se maneja por REEMISIÓN
        // (anular + crear nueva), no re-cobrando el saldo. EnsurePayableAsync es idempotente por invoice.Id.
        // Nada por cobrar (ya pagada/anulada o total 0) ⇒ no se toca el link.
        var totalCents = invoice.Total.AmountCents;
        if (invoice.Status != InvoiceStatus.Paid && invoice.Status != InvoiceStatus.Voided && totalCents > 0)
        {
            var ensured = await paymentLinks.EnsurePayableAsync(
                totalCents,
                invoice.Currency,
                invoice.Id,
                command.TenantId,
                invoice.InvoiceNumber,
                ct
            );
            if (ensured.IsFailure)
                throw new InvalidOperationException(
                    $"Ensure payable failed: {ensured.Error.Code} - {ensured.Error.Message}"
                );

            // Idempotente por ExternalPayableId: en un re-sync (link ya adjunto) es no-op — el monto lo
            // actualizó PaymentClient, no cambia la URL.
            invoice.AttachPaymentLink(
                ensured.Value.PayableId,
                ensured.Value.CheckoutUrl,
                clock.GetUtcNow().UtcDateTime
            );
            await unitOfWork.SaveChangesAsync(ct);
        }

        // Paso siguiente del pipeline (outbox durable): generar el PDF con la URL ya persistida.
        bus.TenantId = command.TenantId.ToString();
        await bus.PublishAsync(new GenerateInvoicePdfCommand(command.TenantId, command.InvoiceId));
    }
}
