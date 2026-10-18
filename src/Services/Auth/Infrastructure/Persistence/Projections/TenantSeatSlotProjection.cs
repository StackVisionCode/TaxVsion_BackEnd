using BuildingBlocks.Domain;

namespace TaxVision.Auth.Infrastructure.Persistence.Projections;

public sealed class TenantSeatSlotProjection : ITenantOwned
{
    private TenantSeatSlotProjection() { }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public int SlotIndex { get; private set; }
    public int EffectiveCap { get; private set; }
    public bool IsGated { get; private set; }
    public DateTime ComputedAtUtc { get; private set; }
    public long RevisionNumber { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static TenantSeatSlotProjection Create(
        Guid tenantId,
        Guid userId,
        int slotIndex,
        int effectiveCap,
        bool isGated,
        DateTime computedAtUtc,
        long revisionNumber
    ) =>
        new()
        {
            TenantId = tenantId,
            UserId = userId,
            SlotIndex = slotIndex,
            EffectiveCap = effectiveCap,
            IsGated = isGated,
            ComputedAtUtc = computedAtUtc,
            RevisionNumber = revisionNumber,
        };

    public void Apply(int slotIndex, int effectiveCap, bool isGated, DateTime computedAtUtc, long revisionNumber)
    {
        SlotIndex = slotIndex;
        EffectiveCap = effectiveCap;
        IsGated = isGated;
        ComputedAtUtc = computedAtUtc;
        RevisionNumber = revisionNumber;
    }
}
