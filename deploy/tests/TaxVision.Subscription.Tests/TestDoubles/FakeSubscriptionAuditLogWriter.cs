using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Audit;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Captura en memoria las entradas de auditoría escritas, para aserciones.</summary>
public sealed class FakeSubscriptionAuditLogWriter : ISubscriptionAuditLogWriter
{
    public List<SubscriptionAuditLog> Entries { get; } = [];

    public Task AppendAsync(SubscriptionAuditLog entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
