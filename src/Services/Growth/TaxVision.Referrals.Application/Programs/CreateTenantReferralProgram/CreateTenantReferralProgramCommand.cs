using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Programs.CreateTenantReferralProgram;

public sealed record TenantReferralProgramPolicyInput(
    int? AttributionWindowDays = null,
    long? MinimumPaymentAmountCents = null,
    string? MinimumPaymentCurrency = null,
    int? WaitingPeriodDays = null,
    int? MaximumRewardsPerReferrerPerCalendarYear = null,
    ReferralRewardType? RewardType = null,
    string? RewardDefinitionKey = null,
    ReferralRefereeBenefitType? RefereeBenefitType = null,
    int? RefereeBenefitPercentageBasisPoints = null,
    long? RefereeBenefitFixedAmountCents = null,
    string? RefereeBenefitCurrency = null,
    int? RefereeBenefitExpirationDays = null
);

public sealed record CreateTenantReferralProgramCommand(
    string ProgramCode,
    string Name,
    DateTime StartsAtUtc,
    DateTime? EndsAtUtc,
    TenantReferralProgramPolicyInput? Policy,
    Guid ActorUserId,
    string IdempotencyKey
);
