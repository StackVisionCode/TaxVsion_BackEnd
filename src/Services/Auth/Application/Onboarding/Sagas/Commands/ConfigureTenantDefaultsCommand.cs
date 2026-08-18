using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.Sagas.Commands;

public sealed record ConfigureTenantDefaultsCommand(Guid OnboardingId, Guid TenantId, Guid UserId, Guid SubscriptionId);

/// <summary>PayFlow (Fase 15) — paso "Defaults" de la Saga, el último. No dispara M2M: los defaults
/// del tenant (roles/permisos base) ya se seedan hoy vía el consumer existente de
/// <c>TenantCreatedIntegrationEvent</c> en Auth (<c>TenantCreatedConsumer</c>), que sigue corriendo
/// sin cambios para tenants creados por onboarding. Este handler cierra el flujo: UoW #8 —
/// <c>MarkStepCompleted(Defaults)</c> avanza <c>CurrentStep</c> a <c>Completed</c>, luego
/// <c>MarkCompleted()</c> + <c>ConsumeRegistrationToken()</c> — y publica
/// <see cref="TenantOnboardingCompletedIntegrationEvent"/>, que la propia Saga consume para
/// marcarse completa (<c>Saga.MarkCompleted()</c>). Fase 17: publica
/// <see cref="OnboardingProvisioningStepFailedIntegrationEvent"/> en vez de descartar en silencio un
/// fallo en cualquiera de los 3 pasos locales — mismo motivo que
/// <c>ProvisionStorageForTenantHandler</c>.</summary>
public static class ConfigureTenantDefaultsHandler
{
    public static async Task<object?> Handle(
        ConfigureTenantDefaultsCommand command,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IOnboardingMetrics metrics,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return null;

        var stepResult = onboarding.MarkStepCompleted(TenantProvisioningStep.Defaults);
        if (stepResult.IsFailure)
            return StepFailed(command, stepResult.Error, correlation);

        var nowUtc = DateTime.UtcNow;
        var completeResult = onboarding.MarkCompleted(nowUtc);
        if (completeResult.IsFailure)
            return StepFailed(command, completeResult.Error, correlation);

        var consumeResult = onboarding.ConsumeRegistrationToken(nowUtc);
        if (consumeResult.IsFailure)
            return StepFailed(command, consumeResult.Error, correlation);

        await unitOfWork.SaveChangesAsync(ct);

        metrics.RecordCompleted();
        metrics.RecordDurationSeconds((nowUtc - onboarding.CreatedAtUtc).TotalSeconds, "completed");

        return new TenantOnboardingCompletedIntegrationEvent
        {
            TenantId = command.TenantId,
            OnboardingId = command.OnboardingId,
            CompletedTenantId = command.TenantId,
            CompletedUserId = command.UserId,
            CompletedSubscriptionId = command.SubscriptionId,
            CorrelationId = correlation.CorrelationId,
        };
    }

    private static OnboardingProvisioningStepFailedIntegrationEvent StepFailed(
        ConfigureTenantDefaultsCommand command,
        BuildingBlocks.Results.Error error,
        ICorrelationContext correlation
    ) =>
        new()
        {
            TenantId = PlatformTenant.Id,
            OnboardingId = command.OnboardingId,
            FailedStep = TenantProvisioningStep.Defaults.ToString(),
            FailureCode = error.Code,
            FailureReason = error.Message,
            CorrelationId = correlation.CorrelationId,
        };
}
