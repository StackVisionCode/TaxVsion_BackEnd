using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Payables.IntegrationEvents;

/// <summary>
/// Factura PAGADA en Billing → no se puede volver a cobrar. Liquida el <c>PayableReference</c> y
/// revoca el <c>PaymentLink</c> vigente, si queda alguno.
///
/// <para>Gemelo de <see cref="InvoiceVoidedConsumer"/>, pero con un estado propio: anulada y pagada
/// no son lo mismo y al cliente hay que decirle cuál de las dos es.</para>
///
/// <para>Sin esto, el resolver de la URL estable acuñaba un link NUEVO cada vez que alguien abría el
/// enlace del PDF de una factura ya cobrada — el link usado deja de ser redimible y la creación
/// perezosa lo tomaba como "no hay link". Camino de doble cobro.</para>
///
/// <para>Idempotente: el evento puede reentregarse.</para>
/// </summary>
public static class InvoicePaidConsumer
{
    public static async Task Handle(
        InvoicePaidIntegrationEvent evt,
        IPayableReferenceRepository payables,
        IPaymentLinkRepository links,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILoggerFactory loggerFactory,
        CancellationToken ct
    )
    {
        var logger = loggerFactory.CreateLogger("PaymentClient.InvoicePaidConsumer");
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var externalRef = evt.InvoiceId.ToString();
            var payable = await payables.GetByExternalReferenceAsync(
                evt.TenantId,
                PaymentPurposeKind.InvoicePayment,
                externalRef,
                ct
            );
            if (payable is null)
            {
                // Cobrada por fuera del checkout (efectivo, transferencia) sin link de pago: nada que liquidar.
                logger.LogInformation("Invoice {InvoiceId} paid: no payable to settle.", evt.InvoiceId);
                return;
            }

            payable.Settle(DateTime.UtcNow);

            // Mata un token ya emitido que siga vivo. Revoke() solo aplica desde Active; en cualquier
            // otro estado es no-op seguro.
            var activeLink = await links.GetActiveByExternalReferenceAsync(evt.TenantId, externalRef, ct);
            activeLink?.Revoke("Invoice paid", DateTime.UtcNow);

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Invoice {InvoiceId} paid: settled payable {PayableId} and revoked the active checkout link (if any).",
                evt.InvoiceId,
                payable.Id
            );
        }
    }
}
