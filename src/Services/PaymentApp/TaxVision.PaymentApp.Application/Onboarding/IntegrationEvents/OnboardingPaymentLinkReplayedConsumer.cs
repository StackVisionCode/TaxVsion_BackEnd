using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.Onboarding.IntegrationEvents;

/// <summary>
/// Auth volvió a anunciar el vínculo de un alta: el pago pasa a su oficina (para que aparezca en su historial)
/// y, si no tiene recibo, toma el que Auth generó (guardado bajo la plataforma). Nunca reemplaza otro recibo. Idempotente: un pago ya
/// vinculado no se vuelve a escribir. Si no hay recibo, <c>MissingReceiptBackfillJob</c> lo pide al ver la oficina.
/// </summary>
public static class OnboardingPaymentLinkReplayedConsumer
{
    public static async Task Handle(
        OnboardingPaymentLinkReplayedIntegrationEvent evt,
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
                return; // Alta cubierta al 100 % por código: no hay pago.

            var moves = payment.TenantId != evt.CreatedTenantId;
            // El recibo de Auth (bajo la plataforma) entra si no hay ninguno, o corrige el dueño del mismo archivo.
            var takesReceipt =
                evt.ReceiptFileId is { } receipt
                && receipt != Guid.Empty
                && (
                    payment.ReceiptFileId is null
                    || (payment.ReceiptFileId == receipt && payment.ReceiptOwnerTenantId != PlatformTenant.Id)
                );
            if (!moves && !takesReceipt)
                return;

            var nowUtc = DateTime.UtcNow;
            if (moves)
            {
                var rehomed = payment.RehomeToTenant(evt.CreatedTenantId, nowUtc);
                if (rehomed.IsFailure)
                {
                    logger.LogWarning(
                        "Could not re-home onboarding payment {PaymentId} to tenant {TenantId}: {Code}.",
                        payment.Id,
                        evt.CreatedTenantId,
                        rehomed.Error.Code
                    );
                    return;
                }
            }

            if (takesReceipt)
            {
                var attached = payment.AttachReceipt(evt.ReceiptFileId!.Value, nowUtc, PlatformTenant.Id);
                if (attached.IsFailure)
                    logger.LogWarning(
                        "Could not attach onboarding receipt to payment {PaymentId}: {Code}.",
                        payment.Id,
                        attached.Error.Code
                    );
            }

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Onboarding payment {PaymentId} linked to tenant {TenantId} (receipt attached: {Receipt}).",
                payment.Id,
                evt.CreatedTenantId,
                takesReceipt
            );
        }
    }
}
