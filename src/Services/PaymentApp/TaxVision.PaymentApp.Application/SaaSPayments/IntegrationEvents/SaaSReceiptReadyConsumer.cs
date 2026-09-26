using BuildingBlocks.Common;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>El recibo ya está guardado: se cuelga del pago, que es de donde el historial lo ofrece.</summary>
public static class SaaSReceiptReadyConsumer
{
    public static async Task Handle(
        SaaSReceiptReadyIntegrationEvent evt,
        ISaaSPaymentRepository payments,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var payment = await payments.GetByIdAsync(evt.SaaSPaymentId, evt.TenantId, ct);
            if (payment is null)
            {
                logger.LogWarning("SaaSReceiptReady for unknown payment {PaymentId}.", evt.SaaSPaymentId);
                return;
            }

            if (payment.ReceiptFileId == evt.ReceiptFileId)
                return; // Redelivery: ya colgado.

            var attached = payment.AttachReceipt(evt.ReceiptFileId, DateTime.UtcNow);
            if (attached.IsFailure)
            {
                logger.LogWarning(
                    "Could not attach receipt {FileId} to payment {PaymentId}: {Code}.",
                    evt.ReceiptFileId,
                    payment.Id,
                    attached.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
