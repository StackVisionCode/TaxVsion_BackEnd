namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando el cobro de una recarga de monedero (originada por
/// <see cref="WalletIntegrationEvents.WalletTopUpDueIntegrationEvent"/>) se confirma. Wallet lo consume
/// para acreditar el saldo (<c>AmountCents * 10_000</c> micros), deduplicando por
/// <c>(SourceService, SaaSPaymentId)</c> (FundingCredit) para no acreditar dos veces.
/// </summary>
public sealed record WalletTopUpPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid TopUpId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string ExternalPaymentReference { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public required DateTime PaidAtUtc { get; init; }
    public required Guid RequestedByUserId { get; init; }
}
