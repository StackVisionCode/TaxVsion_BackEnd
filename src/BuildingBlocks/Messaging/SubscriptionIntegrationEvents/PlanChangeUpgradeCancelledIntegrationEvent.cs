namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription cuando el usuario cancela un upgrade AwaitingPayment antes de pagar.</summary>
public sealed record PlanChangeUpgradeCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid PlanChangeRequestId { get; init; }
    public Guid? SaaSPaymentId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? Reason { get; init; }
}
