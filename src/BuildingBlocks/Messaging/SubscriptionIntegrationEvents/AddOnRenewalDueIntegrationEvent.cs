using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Intent publicado por Subscription cuando un add-on necesita cobrarse para
/// renovarse. Independiente de SubscriptionRenewalDue y de SeatRenewalDue.</summary>
[MessageIdentity("BuildingBlocks.Messaging.SubscriptionIntegrationEvents.AddOnRenewalDueIntegrationEvent")]
public sealed record AddOnRenewalDueIntegrationEvent : IntegrationEvent
{
    public required Guid TenantAddOnId { get; init; }
    public required string AddOnCode { get; init; }
    public required DateTime PeriodStartUtc { get; init; }
    public required DateTime PeriodEndUtc { get; init; }
    public required string IdempotencyKey { get; init; }

    /// <summary>Precio estampado en el add-on al momento de su contratación (fuente de
    /// verdad del pricing) — PaymentApp nunca calcula precios.</summary>
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
}
