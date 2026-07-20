using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Programs.CreateTenantReferralProgram;

public sealed record TenantReferralProgramPolicyInput(
    int? AttributionWindowDays = null,
    long? MinimumPaymentAmountCents = null,
    string? MinimumPaymentCurrency = null,
    int? WaitingPeriodDays = null,
    int? MaximumRewardsPerReferrerPerCalendarYear = null,
    ReferralRewardType? RewardType = null,
    string? RewardDefinitionKey = null
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
