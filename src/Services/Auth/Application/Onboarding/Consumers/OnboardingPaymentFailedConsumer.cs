using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.Consumers;

/// <summary>
/// PayFlow (auditoría F23) — cierra un gap real: <c>TenantOnboarding.MarkPaymentFailed</c> existe
/// desde la Fase 9 (ver doc-comment de <see cref="OnboardingPaymentFailedIntegrationEvent"/>, que
/// desde Fase 8 dice explícitamente "Auth lo consume para marcar el TenantOnboarding como
/// PaymentFailed"), y PaymentApp publica el evento desde Fase 8
/// (<c>ProcessStripeWebhookHandler</c>) — pero ningún consumer lo procesaba en ningún servicio. Sin
/// esto, un pago fallido (declinado, 3-D Secure abandonado) dejaba el <c>TenantOnboarding</c>
/// congelado en <c>PaymentProcessing</c> para siempre en vez de transicionar a
/// <c>PaymentFailed</c> — un estado que el propio aggregate ya sabía representar.
/// </summary>
public static class OnboardingPaymentFailedConsumer
{
    public static async Task Handle(
        OnboardingPaymentFailedIntegrationEvent evt,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<OnboardingPaymentFailedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
        );

        var onboarding = await onboardings.GetByIdAsync(evt.OnboardingId, ct);
        if (onboarding is null)
        {
            logger.LogWarning(
                "OnboardingPaymentFailed for unknown onboarding {OnboardingId}; ignoring.",
                evt.OnboardingId
            );
            return;
        }

        var result = onboarding.MarkPaymentFailed(evt.FailureReason);
        if (result.IsFailure)
        {
            // Replay de un evento ya procesado, o el onboarding avanzó por otro camino mientras
            // tanto (ej. el pago terminó completándose en un reintento posterior del pagador) —
            // en ambos casos MarkPaymentFailed rechaza la transición y no hay nada más que hacer.
            logger.LogWarning(
                "MarkPaymentFailed failed for onboarding {OnboardingId}: {ErrorCode}",
                evt.OnboardingId,
                result.Error.Code
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Onboarding {OnboardingId} marked PaymentFailed: {FailureCode} — {FailureReason}.",
            evt.OnboardingId,
            evt.FailureCode,
            evt.FailureReason
        );
    }
}
