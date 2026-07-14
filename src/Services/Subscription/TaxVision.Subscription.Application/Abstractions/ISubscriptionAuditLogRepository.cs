using TaxVision.Subscription.Domain.Audit;

namespace TaxVision.Subscription.Application.Abstractions;

public interface ISubscriptionAuditLogRepository
{
    Task<(IReadOnlyList<SubscriptionAuditLog> Items, int TotalCount)> SearchAsync(
        Guid tenantId,
        string? aggregateType,
        Guid? aggregateId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
