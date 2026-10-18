using TaxVision.Auth.Application.SlotGating.Abstractions;

namespace TaxVision.Auth.Application.SlotGating;

public sealed record TenantSeatSlotCandidate(Guid UserId, bool IsOwner, DateTime? SlotPriorityAt);

public static class TenantSeatSlotCalculator
{
    public static IReadOnlyList<TenantSeatSlotProjectionRecord> Calculate(
        Guid tenantId,
        IReadOnlyCollection<TenantSeatSlotCandidate> candidates,
        int effectiveCap,
        DateTime computedAtUtc,
        long revisionNumber
    )
    {
        var cap = Math.Max(0, effectiveCap);
        var ordered = candidates
            .OrderByDescending(candidate => candidate.IsOwner)
            .ThenBy(candidate => candidate.SlotPriorityAt ?? DateTime.MaxValue)
            .ThenBy(candidate => candidate.UserId)
            .ToArray();

        var result = new TenantSeatSlotProjectionRecord[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            var candidate = ordered[index];
            var slotIndex = index + 1;
            result[index] = new TenantSeatSlotProjectionRecord(
                tenantId,
                candidate.UserId,
                slotIndex,
                cap,
                !candidate.IsOwner && slotIndex > cap,
                computedAtUtc,
                revisionNumber
            );
        }

        return result;
    }
}
