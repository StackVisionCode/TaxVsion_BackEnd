using Wolverine.Persistence.Sagas;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 15) — publicado por la Saga (<c>TenantOnboardingProcessManager</c>) cuando la
/// llamada M2M de un paso de provisioning falla al ser despachada (p.ej. Tenant/Subscription
/// inalcanzable). Un solo tipo de evento reusado para los 3 pasos M2M (Tenant, TenantAdmin,
/// Subscription) en vez de un evento *Failed por paso — clasificación transient/permanent y retry
/// quedan para Fase 17 (<c>FailureClassifier</c>); por ahora la Saga solo registra el fallo en
/// <c>TenantOnboarding.MarkProvisioningFailed</c> y NO se auto-completa (permanece viva a la espera
/// de un futuro comando de resume).
/// </summary>
public sealed record OnboardingProvisioningStepFailedIntegrationEvent : IntegrationEvent
{
    [SagaIdentity]
    public required Guid OnboardingId { get; init; }

    public required string FailedStep { get; init; }
    public required string FailureCode { get; init; }
    public required string FailureReason { get; init; }
}
