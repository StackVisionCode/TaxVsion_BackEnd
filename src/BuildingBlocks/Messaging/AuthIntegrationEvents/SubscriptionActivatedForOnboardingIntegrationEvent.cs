using Wolverine.Persistence.Sagas;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 16) — publicado por Subscription tras activar la suscripción de un onboarding
/// pago-primero (<c>POST subscriptions/internal/activate-from-onboarding</c>) directamente en
/// <c>Active</c> (no <c>Trialing</c> — el cliente ya pagó). Consumido por
/// <c>TaxVision.Auth.Application.Onboarding.Sagas.TenantOnboardingProcessManager</c> (Fase 15) para
/// avanzar al paso <c>CloudStorage</c>.
/// </summary>
public sealed record SubscriptionActivatedForOnboardingIntegrationEvent : IntegrationEvent
{
    [SagaIdentity]
    public required Guid OnboardingId { get; init; }

    public required Guid CreatedSubscriptionId { get; init; }
}
