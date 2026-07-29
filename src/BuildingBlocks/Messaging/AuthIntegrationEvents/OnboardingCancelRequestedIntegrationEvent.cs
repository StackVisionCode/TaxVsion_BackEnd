namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 17) — publicado por <c>CancelAndRefundOnboardingHandler</c> (Auth,
/// <c>OnboardingAdminController</c>) junto con <see cref="OnboardingRefundRequestedIntegrationEvent"/>
/// cuando un onboarding en <c>ProvisioningFailed</c>/<c>ManualReview</c> se cancela y reembolsa.
/// Compensa los recursos que ya llegaron a existir antes del fallo — cada campo es opcional porque el
/// fallo pudo ocurrir antes de que ese paso corriera: <see cref="TenantId"/> null → Tenant nunca se
/// consume este evento (nada que cerrar); <see cref="UserId"/> null → Auth no desactiva a nadie;
/// <see cref="SubscriptionId"/> null → Subscription no cancela nada. Los 3 consumers son
/// no-ops idempotentes si el campo que les toca es null o el recurso ya está en el estado terminal.
/// </summary>
public sealed record OnboardingCancelRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required string Reason { get; init; }
    public Guid? OnboardingTenantId { get; init; }
    public Guid? OnboardingUserId { get; init; }
    public Guid? OnboardingSubscriptionId { get; init; }
}
