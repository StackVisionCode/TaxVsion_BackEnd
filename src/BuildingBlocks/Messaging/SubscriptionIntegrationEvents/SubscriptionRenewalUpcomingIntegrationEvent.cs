namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription cuando faltan 7, 3 o 1 día(s) para que la
/// suscripción base de un tenant se renueve. Notification decide plantilla y destinatario
/// (normalmente el Tenant Admin).</summary>
public sealed record SubscriptionRenewalUpcomingIntegrationEvent : IntegrationEvent
{
    public required Guid TenantSubscriptionId { get; init; }
    public required DateTime DueAtUtc { get; init; }
    public required int DaysUntilDue { get; init; }
    public required string PlanCode { get; init; }
}
