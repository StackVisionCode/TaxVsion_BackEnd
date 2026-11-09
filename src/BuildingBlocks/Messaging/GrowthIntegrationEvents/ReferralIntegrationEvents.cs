using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.GrowthIntegrationEvents;

[MessageIdentity("BuildingBlocks.Messaging.GrowthIntegrationEvents.ReferralQualifiedIntegrationEvent")]
public sealed record ReferralQualifiedIntegrationEvent : GrowthIntegrationEvent
{
    public override string EventType => "growth.referrals.qualified";
    public required Guid AttributionId { get; init; }
    public required Guid QualificationId { get; init; }
    public required string ProgramType { get; init; }
    public required Guid QualifyingPaymentId { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.GrowthIntegrationEvents.ReferralRewardGrantedIntegrationEvent")]
public sealed record ReferralRewardGrantedIntegrationEvent : GrowthIntegrationEvent
{
    public override string EventType => "growth.referrals.reward_granted";
    public required Guid RewardCaseId { get; init; }
    public required Guid GrantId { get; init; }
    public required string BenefitType { get; init; }
    public required string MaterializedBy { get; init; }
    public required Guid MaterializedReferenceId { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.GrowthIntegrationEvents.ReferralRewardRejectedIntegrationEvent")]
public sealed record ReferralRewardRejectedIntegrationEvent : GrowthIntegrationEvent
{
    public override string EventType => "growth.referrals.reward_rejected";
    public required Guid RewardCaseId { get; init; }
    public required Guid GrantId { get; init; }
    public required string ReasonCode { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.GrowthIntegrationEvents.ReferralRewardReversedIntegrationEvent")]
public sealed record ReferralRewardReversedIntegrationEvent : GrowthIntegrationEvent
{
    public override string EventType => "growth.referrals.reward_reversed";
    public required Guid RewardCaseId { get; init; }
    public required Guid GrantId { get; init; }
    public required Guid SourceFinancialEventId { get; init; }
    public required string ReasonCode { get; init; }
}
