namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Publicado por el servicio Subscription cuando una suscripción se activa
/// (alta inicial, reactivación tras pago o cambio de plan aplicado).
/// Auth lo consume para proyectar los límites del plan.
/// </summary>
public sealed record SubscriptionActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SubscribedTenantId { get; init; }
    public required string PlanCode { get; init; }
    public required int MaxUsers { get; init; }
    public required int MaxPendingInvitations { get; init; }
    public long StorageQuotaBytes { get; init; }
    public string[] EnabledModules { get; init; } = [];
    public DateTime? TrialEndsAtUtc { get; init; }
}
