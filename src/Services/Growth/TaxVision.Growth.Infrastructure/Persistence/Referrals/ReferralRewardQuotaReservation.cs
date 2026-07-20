using BuildingBlocks.Domain;

namespace TaxVision.Growth.Infrastructure.Persistence.Referrals;

/// <summary>
/// Durable idempotency marker for one qualification's quota consumption.
/// It is inserted and counted in the caller's business transaction.
/// </summary>
public sealed class ReferralRewardQuotaReservation : TenantEntity
{
    public Guid ProgramId { get; private set; }
    public Guid ReferrerId { get; private set; }
    public int CalendarYear { get; private set; }
    public Guid QualificationId { get; private set; }
    public DateTime ReservedAtUtc { get; private set; }

    private ReferralRewardQuotaReservation() { }
}
