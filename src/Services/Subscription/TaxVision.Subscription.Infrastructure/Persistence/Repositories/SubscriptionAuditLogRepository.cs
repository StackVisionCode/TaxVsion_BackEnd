using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Audit;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionAuditLogRepository(SubscriptionDbContext db) : ISubscriptionAuditLogRepository
{
    public async Task<(IReadOnlyList<SubscriptionAuditLog> Items, int TotalCount)> SearchAsync(
        Guid tenantId,
        string? aggregateType,
        Guid? aggregateId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = db.AuditLogs.AsNoTracking().Where(entry => entry.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(aggregateType))
            query = query.Where(entry => entry.AggregateType == aggregateType);

        if (aggregateId is not null)
            query = query.Where(entry => entry.AggregateId == aggregateId);

        if (fromUtc is not null)
            query = query.Where(entry => entry.OccurredAtUtc >= fromUtc);

        if (toUtc is not null)
            query = query.Where(entry => entry.OccurredAtUtc <= toUtc);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
