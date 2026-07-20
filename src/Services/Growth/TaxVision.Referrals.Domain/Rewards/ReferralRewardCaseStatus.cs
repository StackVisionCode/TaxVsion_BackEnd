namespace TaxVision.Referrals.Domain.Rewards;

public enum ReferralRewardCaseStatus
{
    Requested,
    PendingGrant,
    Granted,
    Vested,
    ClawbackPending,
    Reversed,
    Failed,
    Cancelled,
    ManualReview,
}
