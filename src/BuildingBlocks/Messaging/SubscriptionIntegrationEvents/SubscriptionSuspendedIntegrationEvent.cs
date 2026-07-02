namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Publicado por Subscription cuando la suscripción queda suspendida
/// (impago, cancelación). Auth aplica "suspensión suave": el login sigue
/// permitido pero las operaciones que consumen plan quedan bloqueadas.
/// </summary>
public sealed record SubscriptionSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid SubscribedTenantId { get; init; }
    public required string Reason { get; init; }
}
