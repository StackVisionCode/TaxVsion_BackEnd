using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// El tenant pagó la renovación a mano: los cobros automáticos que seguían en reintento se cancelan
/// para que el dunning no cobre dos veces lo mismo. Sin publicar nada: Subscription ya lo sabe.
/// </summary>
public static class RenewalChargesSupersededConsumer
{
    public const string SupersededReason = "Paid with another payment.";

    public static async Task Handle(
        RenewalChargesSupersededIntegrationEvent evt,
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
            var cancelled = 0;
            foreach (var key in evt.IdempotencyKeys)
            {
                var payment = await payments.GetByIdempotencyKeyAsync(key, ct);
                if (!ShouldCancel(payment, evt.TenantId))
                    continue;

                if (payment!.MarkCancelled(SupersededReason, DateTime.UtcNow).IsSuccess)
                    cancelled++;
            }

            if (cancelled == 0)
                return;

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Cancelled {Count} automatic renewal charge(s) of tenant {TenantId}: paid with another payment.",
                cancelled,
                evt.TenantId
            );
        }
    }

    // Solo lo que sigue en reintento: un fallo ya abandonado no se reescribe como "pagado", y un cobro en
    // curso (Processing) se deja porque cancelarlo acá no frena al provider.
    private static bool ShouldCancel(SaaSPayment? payment, Guid tenantId) =>
        payment is not null
        && payment.TenantId == tenantId
        && payment.Status == PaymentStatus.Failed
        && payment.NextRetryAtUtc is not null;
}
