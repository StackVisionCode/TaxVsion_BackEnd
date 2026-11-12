using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription cuando faltan 7, 3 o 1 día(s) para que la
/// suscripción base de un tenant se renueve. Notification decide plantilla y destinatario
/// (normalmente el Tenant Admin).</summary>
[MessageIdentity("BuildingBlocks.Messaging.SubscriptionIntegrationEvents.SubscriptionRenewalUpcomingIntegrationEvent")]
public sealed record SubscriptionRenewalUpcomingIntegrationEvent : IntegrationEvent
{
    public required Guid TenantSubscriptionId { get; init; }
    public required DateTime DueAtUtc { get; init; }
    public required int DaysUntilDue { get; init; }
    public required string PlanCode { get; init; }

    /// <summary>Nombre visible del plan (aditivo). Null en mensajes viejos: el consumidor cae al código.</summary>
    public string? PlanName { get; init; }

    /// <summary>Total que se cobrará en la renovación (plan más add-ons y asientos activos), y su ciclo. Opcionales.</summary>
    public long? AmountCents { get; init; }
    public string? Currency { get; init; }
    public string? BillingCycle { get; init; }
}
