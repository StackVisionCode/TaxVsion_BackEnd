using TaxVision.Connectors.Application.Abstractions;

namespace TaxVision.Connectors.Infrastructure.Locking;

/// <summary>Sin Redis (dev de un solo nodo) no hay sync concurrente que marcar.</summary>
public sealed class NoOpSyncPendingFlag : ISyncPendingFlag
{
    public Task MarkAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> TryConsumeAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(false);
}
