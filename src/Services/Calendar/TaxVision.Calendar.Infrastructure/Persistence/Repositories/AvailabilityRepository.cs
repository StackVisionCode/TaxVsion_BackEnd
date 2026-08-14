using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Domain.Availability;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

public sealed class AvailabilityRepository(CalendarDbContext context) : IAvailabilityRepository
{
    public async Task<IReadOnlyList<AvailabilityRule>> ListRulesAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await context
            .AvailabilityRules.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.UserId == userId && r.IsActive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BlockedTime>> ListBlocksAsync(
        Guid tenantId,
        Guid userId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default
    ) =>
        await context
            .BlockedTimes.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.UserId == userId && b.StartUtc < toUtc && b.EndUtc > fromUtc)
            .ToListAsync(ct);

    public void AddRule(AvailabilityRule rule) => context.AvailabilityRules.Add(rule);

    public void AddBlock(BlockedTime block) => context.BlockedTimes.Add(block);
}
