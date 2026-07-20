namespace BuildingBlocks.Messaging.GrowthIntegrationEvents;

public sealed record CodeReservationCommittedIntegrationEvent : GrowthIntegrationEvent
{
    public override string EventType => "growth.codes.reservation_committed";
    public required Guid ReservationId { get; init; }
    public required Guid RedemptionId { get; init; }
    public required string PaymentSource { get; init; }
    public required Guid RelatedPaymentId { get; init; }
    public required long GrossAmountCents { get; init; }
    public required long DiscountAmountCents { get; init; }
    public required long NetAmountCents { get; init; }
    public required string Currency { get; init; }
}

public sealed record CodeRedemptionCompensatedIntegrationEvent : GrowthIntegrationEvent
{
    public override string EventType => "growth.codes.redemption_compensated";
    public required Guid RedemptionId { get; init; }
    public required Guid CompensationId { get; init; }
    public required string CompensationType { get; init; }
    public required long CompensatedAmountCents { get; init; }
    public required string Currency { get; init; }
    public required Guid SourceEventId { get; init; }
}

public sealed record BenefitGrantRecordedIntegrationEvent : GrowthIntegrationEvent
{
    public override string EventType => "growth.codes.benefit_grant_recorded";
    public required Guid GrantId { get; init; }
    public required string BenefitType { get; init; }
    public required string TargetType { get; init; }
    public required Guid TargetId { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
}
