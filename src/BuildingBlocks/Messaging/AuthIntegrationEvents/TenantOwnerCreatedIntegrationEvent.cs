using Wolverine.Persistence.Sagas;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 16) — publicado por el endpoint interno de Auth
/// (<c>POST internal/tenants/{tenantId}/owners</c>) tras crear el TenantAdmin de un onboarding
/// pago-primero. El password nunca cruza este evento — ya fue hasheado y persistido por el propio
/// endpoint antes de publicar. Consumido por
/// <c>TaxVision.Auth.Application.Onboarding.Sagas.TenantOnboardingProcessManager</c> (Fase 15) para
/// avanzar al paso <c>Subscription</c>.
/// </summary>
public sealed record TenantOwnerCreatedIntegrationEvent : IntegrationEvent
{
    [SagaIdentity]
    public required Guid OnboardingId { get; init; }

    public required Guid CreatedUserId { get; init; }
}
