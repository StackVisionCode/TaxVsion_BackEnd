using TaxVision.Subscription.Domain.Audit;

namespace TaxVision.Subscription.Application.Abstractions;

public interface ISubscriptionAuditLogWriter
{
    Task AppendAsync(SubscriptionAuditLog entry, CancellationToken ct = default);
}
