using TaxVision.Referrals.Domain.Attributions;

namespace TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;

public sealed record CreateReferralAttributionResult(
    Guid AttributionId,
    ReferralAttributionStatus Status,
    bool WasReplay
);
