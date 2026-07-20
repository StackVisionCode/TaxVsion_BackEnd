using TaxVision.Referrals.Domain.Programs;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Programs.Common;

public sealed record TenantReferralProgramPolicyResult(
    int AttributionWindowDays,
    QualifyingPaymentSource PaymentSource,
    QualifyingEventRule QualifyingEvent,
    long MinimumPaymentAmountCents,
    string? MinimumPaymentCurrency,
    int WaitingPeriodDays,
    int MaximumRewardsPerReferrerPerCalendarYear,
    ReferralRewardType RewardType,
    string RewardDefinitionKey
);

public sealed record TenantReferralProgramResult(
    Guid ProgramId,
    string ProgramCode,
    string Name,
    ReferralProgramScope Scope,
    ReferralFlowType Flow,
    ReferralProgramStatus Status,
    int PolicyVersion,
    TenantReferralProgramPolicyResult Policy,
    DateTime StartsAtUtc,
    DateTime? EndsAtUtc
)
{
    public static TenantReferralProgramResult From(ReferralProgram program) =>
        new(
            program.Id,
            program.ProgramCode,
            program.Name,
            program.ScopeType,
            program.FlowType,
            program.Status,
            program.PolicyVersion,
            new TenantReferralProgramPolicyResult(
                program.Policy.AttributionWindowDays,
                program.Policy.PaymentSource,
                program.Policy.QualifyingEvent,
                program.Policy.MinimumPaymentAmountCents,
                program.Policy.MinimumPaymentCurrency,
                program.Policy.WaitingPeriodDays,
                program.Policy.MaximumRewardsPerReferrerPerCalendarYear,
                program.Policy.RewardType,
                program.Policy.RewardDefinitionKey
            ),
            program.StartsAtUtc,
            program.EndsAtUtc
        );
}
