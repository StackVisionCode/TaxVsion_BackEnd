namespace BuildingBlocks.Messaging.GrowthIntegrationEvents;

/// <summary>Comando dirigido a Subscription. No representa un hecho ocurrido.</summary>
public sealed record GrantReferralRewardCommand
{
    public required Guid CommandId { get; init; }
    public required int CommandVersion { get; init; } = 1;
    public required DateTime RequestedAt { get; init; }
    public required string CorrelationId { get; init; }
    public required string CausationId { get; init; }
    public required string TraceId { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid AggregateId { get; init; }
    public required long AggregateVersion { get; init; }
    public required Guid GrantId { get; init; }
    public required Guid RewardCaseId { get; init; }
    public required string BenefitType { get; init; }
    public required string TargetType { get; init; }
    public required Guid TargetId { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
}
