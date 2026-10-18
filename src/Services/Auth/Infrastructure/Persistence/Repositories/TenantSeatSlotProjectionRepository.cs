using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.SlotGating.Abstractions;
using TaxVision.Auth.Infrastructure.Persistence.Projections;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class TenantSeatSlotProjectionRepository(AuthDbContext db) : ITenantSeatSlotProjectionRepository
{
    public async Task<IReadOnlyList<TenantSeatSlotProjectionRecord>> GetByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await db
            .TenantSeatSlotProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .OrderBy(row => row.SlotIndex)
            .Select(row => ToRecord(row))
            .ToListAsync(ct);

    public Task<TenantSeatSlotProjectionRecord?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        db
            .TenantSeatSlotProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.UserId == userId)
            .Select(row => ToRecord(row))
            .FirstOrDefaultAsync(ct);

    public async Task ReplaceAsync(
        Guid tenantId,
        IReadOnlyList<TenantSeatSlotProjectionRecord> projections,
        CancellationToken ct = default
    )
    {
        await db
            .TenantSeatSlotProjections.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);

        await db.TenantSeatSlotProjections.AddRangeAsync(
            projections.Select(row =>
                TenantSeatSlotProjection.Create(
                    row.TenantId,
                    row.UserId,
                    row.SlotIndex,
                    row.EffectiveCap,
                    row.IsGated,
                    row.ComputedAtUtc,
                    row.RevisionNumber
                )
            ),
            ct
        );
    }

    private static TenantSeatSlotProjectionRecord ToRecord(TenantSeatSlotProjection row) =>
        new(
            row.TenantId,
            row.UserId,
            row.SlotIndex,
            row.EffectiveCap,
            row.IsGated,
            row.ComputedAtUtc,
            row.RevisionNumber
        );
}
