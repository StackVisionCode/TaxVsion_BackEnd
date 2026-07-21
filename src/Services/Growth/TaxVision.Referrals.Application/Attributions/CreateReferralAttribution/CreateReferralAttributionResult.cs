using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;

/// <summary>
/// The RefereeBenefit* fields describe the program's policy terms (never a secret) so
/// that Growth.Api's ReferralsController — the only layer that knows both the Referrals
/// and Codes bounded contexts — can decide whether to issue a discount CodeDefinition for
/// the referee right after the attribution succeeds. Null when the program has no referee
/// benefit configured.
/// </summary>
public sealed record CreateReferralAttributionResult(
    Guid AttributionId,
    ReferralAttributionStatus Status,
    bool WasReplay,
    ReferralRefereeBenefitType? RefereeBenefitType,
    int? RefereeBenefitPercentageBasisPoints,
    long? RefereeBenefitFixedAmountCents,
    string? RefereeBenefitCurrency,
    int RefereeBenefitExpirationDays
);
