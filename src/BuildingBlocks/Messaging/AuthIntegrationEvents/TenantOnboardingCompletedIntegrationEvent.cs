using Wolverine.Persistence.Sagas;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 15) — publicado por <c>ConfigureTenantDefaultsCommand</c> (último paso local de la
/// Saga) tras <c>TenantOnboarding.MarkCompleted() + ConsumeRegistrationToken()</c> (UoW #8). Marca el
/// fin del flujo pago-primero: el tenant está operativo. <see cref="IntegrationEvent.TenantId"/> ya
/// es el tenant real (no <c>PlatformTenant.Id</c>). La propia
/// <c>TenantOnboardingProcessManager</c> lo consume para llamar <c>MarkCompleted()</c> sobre sí misma
/// (fin de la saga de Wolverine) — otros servicios (p.ej. Notification, para el email de
/// bienvenida) pueden suscribirse a este evento sin acoplarse a la Saga.
/// </summary>
public sealed record TenantOnboardingCompletedIntegrationEvent : IntegrationEvent
{
    [SagaIdentity]
    public required Guid OnboardingId { get; init; }

    public required Guid CompletedTenantId { get; init; }
    public required Guid CompletedUserId { get; init; }
    public required Guid CompletedSubscriptionId { get; init; }
}
