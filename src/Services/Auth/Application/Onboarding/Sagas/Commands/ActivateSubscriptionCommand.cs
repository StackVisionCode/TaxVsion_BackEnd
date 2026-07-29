using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.Sagas.Commands;

public sealed record ActivateSubscriptionCommand(Guid OnboardingId, Guid TenantId, Guid PlanId);

/// <summary>PayFlow (Fase 15) — tercer paso de la Saga: dispara la activación de la suscripción
/// (Active, no Trialing) vía M2M fire-and-forget. La Saga avanza cuando le llega
/// <c>SubscriptionActivatedForOnboardingIntegrationEvent</c>.</summary>
public static class ActivateSubscriptionHandler
{
    public static async Task<OnboardingProvisioningStepFailedIntegrationEvent?> Handle(
        ActivateSubscriptionCommand command,
        ISubscriptionActivationClient client,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var result = await client.ActivateAsync(
            new ActivateSubscriptionForOnboardingRequest(command.OnboardingId, command.TenantId, command.PlanId),
            ct
        );

        if (result.IsSuccess)
            return null;

        return new OnboardingProvisioningStepFailedIntegrationEvent
        {
            TenantId = PlatformTenant.Id,
            OnboardingId = command.OnboardingId,
            FailedStep = TenantProvisioningStep.Subscription.ToString(),
            FailureCode = result.Error.Code,
            FailureReason = result.Error.Message,
            CorrelationId = correlation.CorrelationId,
        };
    }
}
