using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Audit;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionAuditLogWriter(SubscriptionDbContext db) : ISubscriptionAuditLogWriter
{
    public async Task AppendAsync(SubscriptionAuditLog entry, CancellationToken ct = default) =>
        await db.AuditLogs.AddAsync(entry, ct);
}
