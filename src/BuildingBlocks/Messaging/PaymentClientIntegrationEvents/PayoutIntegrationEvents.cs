namespace BuildingBlocks.Messaging.PaymentClientIntegrationEvents;

public sealed record PayoutCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid PayoutScheduleId { get; init; }
    public required string ProviderPayoutReference { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public required DateTime PaidAtUtc { get; init; }
}

public sealed record PayoutFailedIntegrationEvent : IntegrationEvent
{
    public required Guid PayoutScheduleId { get; init; }
    public required string ProviderPayoutReference { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public required string FailureReason { get; init; }
    public required DateTime FailedAtUtc { get; init; }
}
