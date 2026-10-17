using System.Collections.Concurrent;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.Concurrency;

public sealed class InMemoryOnboardingReconcileSerializer : IOnboardingReconcileSerializer
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> AcquireAsync(Guid onboardingId, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(onboardingId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Release(this, onboardingId, gate);
    }

    private sealed class Release(InMemoryOnboardingReconcileSerializer owner, Guid id, SemaphoreSlim gate)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            // Deja la entrada: dos reconciles seguidos del mismo onboarding (reintento legítimo tras un
            // fallo cosmético) reutilizan el mismo semaforo. Un sweeper podría limpiar entradas con
            // CurrentCount==1 sin waiters si el volumen de onboardings a largo plazo lo requiere.
            _ = owner;
            _ = id;
            return ValueTask.CompletedTask;
        }
    }
}
