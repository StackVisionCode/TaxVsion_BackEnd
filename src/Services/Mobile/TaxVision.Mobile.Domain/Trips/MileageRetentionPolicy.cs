using BuildingBlocks.Domain;

namespace TaxVision.Mobile.Domain.Trips;

/// <summary>Per-tenant retention period for precise GPS coordinates. Null means no automatic purge.</summary>
public sealed class MileageRetentionPolicy : TenantEntity
{
    private MileageRetentionPolicy() { }

    public int? RetentionDays { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static MileageRetentionPolicy Create(Guid tenantId, int? retentionDays, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty || retentionDays is <= 0 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(retentionDays));
        var policy = new MileageRetentionPolicy
        {
            Id = tenantId,
            RetentionDays = retentionDays,
            UpdatedAtUtc = nowUtc,
        };
        policy.SetTenant(tenantId);
        return policy;
    }

    public void Update(int? retentionDays, DateTime nowUtc)
    {
        if (retentionDays is <= 0 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(retentionDays));
        RetentionDays = retentionDays;
        UpdatedAtUtc = nowUtc;
    }
}
