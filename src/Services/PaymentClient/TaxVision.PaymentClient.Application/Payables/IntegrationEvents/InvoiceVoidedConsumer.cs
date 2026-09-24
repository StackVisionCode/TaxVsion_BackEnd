using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Payables.IntegrationEvents;

/// <summary>
/// Factura ANULADA en Billing (<c>InvoiceVoidedIntegrationEvent</c>) → una factura anulada no se debe
/// poder pagar. Revoca el <c>PayableReference</c> (el resolver deja de acuñar links nuevos) y anula el
/// <c>PaymentLink</c> de checkout vigente, si hay, para que un token ya emitido (<c>/pay/{token}</c>)
/// deje de funcionar de inmediato. Idempotente: reprocesar el evento no rompe nada.
/// </summary>
public static class InvoiceVoidedConsumer
{
    public static async Task Handle(
        InvoiceVoidedIntegrationEvent evt,
        IPayableReferenceRepository payables,
        IPaymentLinkRepository links,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILoggerFactory loggerFactory,
        CancellationToken ct
    )
    {
        var logger = loggerFactory.CreateLogger("PaymentClient.InvoiceVoidedConsumer");
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
                // La factura nunca tuvo link de pago (p. ej. anulada antes de emitirse) → nada que revocar.
                logger.LogInformation("Invoice {InvoiceId} voided: no payable to revoke.", evt.InvoiceId);
                return;
            }

            payable.Revoke(DateTime.UtcNow);

            // Matar el link de checkout vigente para invalidar un token ya emitido. Revoke() solo aplica
            // desde Active; en cualquier otro estado es no-op seguro.
            var activeLink = await links.GetActiveByExternalReferenceAsync(evt.TenantId, externalRef, ct);
            activeLink?.Revoke("Invoice voided", DateTime.UtcNow);

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Invoice {InvoiceId} voided: revoked payable {PayableId} and active checkout link (if any).",
                evt.InvoiceId,
                payable.Id
            );
        }
    }
}
