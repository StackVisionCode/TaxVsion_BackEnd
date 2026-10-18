namespace TaxVision.Auth.Application.SlotGating.Abstractions;

public sealed record TenantSeatSlotProjectionRecord(
    Guid TenantId,
    Guid UserId,
    int SlotIndex,
    int EffectiveCap,
    bool IsGated,
    DateTime ComputedAtUtc,
    long RevisionNumber
);

public interface ITenantSeatSlotProjectionRepository
{
    Task<IReadOnlyList<TenantSeatSlotProjectionRecord>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task<TenantSeatSlotProjectionRecord?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task ReplaceAsync(
        Guid tenantId,
        IReadOnlyList<TenantSeatSlotProjectionRecord> projections,
        CancellationToken ct = default
    );
}
