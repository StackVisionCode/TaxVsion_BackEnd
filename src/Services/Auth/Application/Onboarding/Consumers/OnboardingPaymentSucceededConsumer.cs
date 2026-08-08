using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Consumers;

/// <summary>
/// PayFlow (Fase 9) — cierra el ciclo pago→token en el carril con cobro real (net &gt; 0). Reacciona al
/// <see cref="OnboardingPaymentSucceededIntegrationEvent"/> de PaymentApp. Delega el camino de éxito
/// (registration token + registration-ready + FINALIZE) en <see cref="OnboardingSuccessCompleter"/>,
/// compartido con el carril $0 (cobertura 100% por código).
/// <para>
/// Gift/Referral: el documento financiero ya NO lo genera Auth. Antes se pedía un recibo directo a
/// Documents (responsabilidad mal ubicada); ahora el FINALIZE le pide a <b>Billing</b> asentar la factura
/// (fuente de verdad financiera) y Documents solo la renderiza.
/// </para>
/// </summary>
public static class OnboardingPaymentSucceededConsumer
{
    public static async Task Handle(
        OnboardingPaymentSucceededIntegrationEvent evt,
        ITenantOnboardingRepository onboardings,
        OnboardingSuccessCompleter successCompleter,
        IPlanCatalogClient planCatalog,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<OnboardingPaymentSucceededIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var onboarding = await LoadAndCompleteOnboardingAsync(evt, onboardings, logger, ct);
            if (onboarding is null)
                return;

            var planName = await planCatalog.GetPlanNameAsync(evt.PlanId, ct);

            var completed = await successCompleter.CompleteAsync(
                onboarding,
                evt.AmountPaidCents,
                evt.Currency,
                paymentId: evt.SaaSPaymentId,
                planName,
                evt.PaidAtUtc,
                correlationId,
                ct
            );
            if (completed.IsFailure)
                return;

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation("Onboarding {OnboardingId} payment completed (net carril).", onboarding.Id);
        }
    }

    private static async Task<TenantOnboarding?> LoadAndCompleteOnboardingAsync(
        OnboardingPaymentSucceededIntegrationEvent evt,
        ITenantOnboardingRepository onboardings,
        ILogger logger,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(evt.OnboardingId, ct);
        if (onboarding is null)
        {
            logger.LogWarning(
                "OnboardingPaymentSucceeded for unknown onboarding {OnboardingId}; ignoring.",
                evt.OnboardingId
            );
            return null;
        }

        var completedResult = onboarding.MarkPaymentCompleted(evt.SaaSPaymentId.ToString("N"), evt.PaidAtUtc);
        if (completedResult.IsFailure)
        {
            // Replay de un evento ya procesado (el onboarding ya avanzó más allá de PaymentCompleted) o
            // inconsistencia real — en ambos casos, no reintentar el resto del flujo.
            logger.LogWarning(
                "MarkPaymentCompleted failed for onboarding {OnboardingId}: {ErrorCode}",
                evt.OnboardingId,
                completedResult.Error.Code
            );
            return null;
        }

        return onboarding;
    }
}
