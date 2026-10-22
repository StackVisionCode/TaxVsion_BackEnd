using TaxVision.Connectors.Application.Abstractions;

namespace TaxVision.Connectors.Infrastructure.Locking;

/// <summary>Sin Redis no hay listener que coordinar: la reconciliación sigue a su ritmo normal.</summary>
public sealed class NoOpMailboxListenerPresence : IMailboxListenerPresence
{
    public Task MarkListeningAsync(Guid accountId, TimeSpan ttl, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> IsListeningAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(false);

    public Task<bool> TryClaimSafetyNetSlotAsync(Guid accountId, TimeSpan interval, CancellationToken ct = default) =>
        Task.FromResult(true);
}
