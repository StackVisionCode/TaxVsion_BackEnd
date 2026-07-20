using BuildingBlocks.Domain;

namespace TaxVision.Growth.Infrastructure.Persistence.Referrals;

/// <summary>
/// SQL-owned concurrency row for the annual referral reward ceiling.
/// TenantId is the T2T referrer tenant whose quota is being consumed.
/// </summary>
public sealed class ReferralRewardQuotaCounter : TenantEntity
{
    public Guid ProgramId { get; private set; }
    public Guid ReferrerId { get; private set; }
    public int CalendarYear { get; private set; }
    public int Maximum { get; private set; }
    public int ReservedCount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ReferralRewardQuotaCounter() { }
}
