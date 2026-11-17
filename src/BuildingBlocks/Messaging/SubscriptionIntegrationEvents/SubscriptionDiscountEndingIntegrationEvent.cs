using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Publicado por Subscription 7 días (calendario de la oficina) antes de que termine un descuento de N meses.
/// Notification avisa al owner en la campana y por correo.
/// </summary>
[MessageIdentity("subscription.discounts.ending.v1")]
public sealed record SubscriptionDiscountEndingIntegrationEvent : IntegrationEvent
{
    public required Guid TenantSubscriptionId { get; init; }
    public required Guid DiscountId { get; init; }
    public required DateTime EndsAtUtc { get; init; }
    public required int DaysUntilEnd { get; init; }

    /// <summary>"Code" o "Admin".</summary>
    public required string Source { get; init; }

    /// <summary>El beneficio: porcentaje en basis points o monto fijo con su moneda.</summary>
    public int? PercentBasisPoints { get; init; }
    public long? AmountCents { get; init; }
    public string? Currency { get; init; }
}
