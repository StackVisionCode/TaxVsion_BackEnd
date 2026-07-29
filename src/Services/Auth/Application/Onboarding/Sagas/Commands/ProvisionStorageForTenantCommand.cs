using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.Sagas.Commands;

public sealed record ProvisionStorageForTenantCommand(
    Guid OnboardingId,
    Guid TenantId,
    Guid UserId,
    Guid SubscriptionId
);

/// <summary>PayFlow (Fase 15) — paso "CloudStorage" de la Saga. A diferencia de Tenant/TenantAdmin/
/// Subscription, este paso NO dispara ningún M2M: el §5 del plan (matriz de impacto) es explícito —
/// "CloudStorage: Cero cambios (Documents ya usa SaveFileRequestedIntegrationEvent, patrón Fase D0
/// existente)". El aprovisionamiento de storage por tenant ya ocurre automáticamente hoy vía el
/// consumer existente de <c>TenantCreatedIntegrationEvent</c> en CloudStorage — no necesita
/// coordinación explícita de esta Saga. Este handler solo avanza el <see cref="TenantOnboarding"/>
/// (UoW local) y encadena al siguiente paso.
/// <para>
/// PayFlow (Fase 17) — antes este handler descartaba en silencio un <c>Result.IsFailure</c> (repo
/// miss, guard de estado inconsistente): el onboarding quedaba <c>Provisioning</c> para siempre, sin
/// <c>FailedStep</c>/<c>FailureCode</c> ni evento — invisible para <c>OnboardingAdminController</c> y
/// el retry scheduler. Ahora publica <see cref="OnboardingProvisioningStepFailedIntegrationEvent"/>
/// igual que los pasos M2M, para que la Saga lo registre y quede accionable.</para></summary>
public static class ProvisionStorageForTenantHandler
{
    public static async Task<object?> Handle(
        ProvisionStorageForTenantCommand command,
        ITenantOnboardingRepository onboardings,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(command.OnboardingId, ct);
        if (onboarding is null)
            return null;

        var result = onboarding.MarkStepCompleted(TenantProvisioningStep.CloudStorage);
        if (result.IsFailure)
        {
            return new OnboardingProvisioningStepFailedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                OnboardingId = command.OnboardingId,
                FailedStep = TenantProvisioningStep.CloudStorage.ToString(),
                FailureCode = result.Error.Code,
                FailureReason = result.Error.Message,
                CorrelationId = correlation.CorrelationId,
            };
        }

        await unitOfWork.SaveChangesAsync(ct);

        return new ActivateSubdomainForTenantCommand(
            command.OnboardingId,
            command.TenantId,
            command.UserId,
            command.SubscriptionId
        );
    }
}
