using Wolverine.Persistence.Sagas;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 16) — publicado por Tenant cuando crea el Tenant real a partir de un onboarding
/// pago-primero (<c>POST /tenants/internal/from-onboarding</c>), en paralelo al
/// <c>TenantCreatedIntegrationEvent</c> normal. Consumido por
/// <c>TaxVision.Auth.Application.Onboarding.Sagas.TenantOnboardingProcessManager</c> (Fase 15) para
/// avanzar al paso <c>TenantAdmin</c>. <see cref="OnboardingId"/> lleva <c>[SagaIdentity]</c> — es la
/// clave de correlación de la Saga, no <see cref="IIntegrationEvent.EventId"/>.
/// <see cref="IntegrationEvent.TenantId"/> ya es el tenant real recién creado (no
/// <c>PlatformTenant.Id</c>): a partir de este evento el resto de la cadena opera dentro del tenant.
/// </summary>
public sealed record TenantCreatedForOnboardingIntegrationEvent : IntegrationEvent
{
    [SagaIdentity]
    public required Guid OnboardingId { get; init; }

    public required Guid CreatedTenantId { get; init; }
}
