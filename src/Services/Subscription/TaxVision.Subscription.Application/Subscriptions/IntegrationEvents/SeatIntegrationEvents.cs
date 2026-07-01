using BuildingBlocks.Messaging;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

// Subscription → Payment: cobrar la compra inicial del seat
public sealed record SeatPurchaseRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid SubscriptionId { get; init; }
    public required Guid SeatId { get; init; }
    public required int Quantity { get; init; }
    public required decimal PricePerSeat { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public required BillingPeriod BillingPeriod { get; init; }
    public required DateTime PeriodStartUtc { get; init; }
    public required DateTime PeriodEndUtc { get; init; }
    public required int BillingAnchorDay { get; init; }
}

// Payment → Subscription: pago del seat confirmado
public sealed record SeatPaymentCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid SubscriptionId { get; init; }
    public required Guid SeatId { get; init; }
    public required Guid InvoiceId { get; init; }
    public required decimal AmountPaid { get; init; }
}

// Payment → Subscription: pago del seat fallido
public sealed record SeatPaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid SubscriptionId { get; init; }
    public required Guid SeatId { get; init; }
    public required string Reason { get; init; } = default!;
}

// Wolverine Scheduled → ciclo del seat venció
public sealed record SeatRenewalDueIntegrationEvent : IntegrationEvent
{
    public required Guid SubscriptionId { get; init; }
    public required Guid SeatId { get; init; }
    public required DateTime ExpectedPeriodEnd { get; init; }
    public required int BillingAnchorDay { get; init; }
}

// Subscription → Payment: cobrar renovación de este seat
public sealed record SeatRenewalPaymentRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid SubscriptionId { get; init; }
    public required Guid SeatId { get; init; }
    public required int Quantity { get; init; }
    public required decimal PricePerSeat { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; } = "USD";
    public required int BillingAnchorDay { get; init; }
    public required DateTime NewPeriodEnd { get; init; }
}

// Payment → Subscription: renovación confirmada
public sealed record SeatRenewalPaymentCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid SubscriptionId { get; init; }
    public required Guid SeatId { get; init; }
    public required Guid InvoiceId { get; init; }
    public required DateTime NewPeriodEnd { get; init; }
    public required int BillingAnchorDay { get; init; }
    // Precio con que se cobró esta renovación (precio vigente del plan al momento del cobro)
    public required decimal PricePerSeat { get; init; }
    public required string Currency { get; init; } = "USD";
}

// Payment → Subscription: renovación fallida
public sealed record SeatRenewalPaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid SubscriptionId { get; init; }
    public required Guid SeatId { get; init; }
    public required string Reason { get; init; } = default!;
}

// Subscription → Auth: cambió TotalAvailableSeats
public sealed record TenantEntitlementsChangedIntegrationEvent : IntegrationEvent
{
    public required Guid SubscriptionId { get; init; }
    public required int TotalAvailableSeats { get; init; }
    public IReadOnlyList<string> FeatureCodes { get; init; } = [];
}

// Wolverine Scheduled → ciclo base venció
public sealed record SubscriptionRenewalDueIntegrationEvent : IntegrationEvent
{
    public required Guid SubscriptionId { get; init; }
    public required DateTime ExpectedPeriodEnd { get; init; }
    public required int BillingAnchorDay { get; init; }
}
