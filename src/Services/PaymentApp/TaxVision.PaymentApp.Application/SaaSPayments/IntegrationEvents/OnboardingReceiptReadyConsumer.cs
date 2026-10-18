using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// Cuelga el ReceiptFileId del SaaSPayment de onboarding. Documents publica
/// SaaSReceiptReady solo cuando Owner="SaaSPayment"; los recibos de onboarding usan
/// Owner="Onboarding" (Auth los attachea al TenantOnboarding), así que billing history
/// no lo veía nunca. Lookup por OnboardingId (único campo que ambos lados comparten).
/// </summary>
public static class OnboardingReceiptReadyConsumer
{
    public static async Task Handle(
        OnboardingReceiptReadyIntegrationEvent evt,
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
            var payment = await payments.GetByOnboardingIdAsync(evt.OnboardingId, ct);
            if (payment is null)
            {
                logger.LogWarning(
                    "OnboardingReceiptReady for onboarding {OnboardingId} but no SaaSPayment found; ignoring.",
                    evt.OnboardingId
                );
                return;
            }

            if (payment.ReceiptFileId == evt.ReceiptFileId)
                return; // Redelivery: ya colgado.

            var attached = payment.AttachReceipt(evt.ReceiptFileId, DateTime.UtcNow);
            if (attached.IsFailure)
            {
                logger.LogWarning(
                    "Could not attach onboarding receipt {FileId} to payment {PaymentId}: {Code}.",
                    evt.ReceiptFileId,
                    payment.Id,
                    attached.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Onboarding receipt {FileId} attached to SaaSPayment {PaymentId} for onboarding {OnboardingId}.",
                evt.ReceiptFileId,
                payment.Id,
                evt.OnboardingId
            );
        }
    }
}
