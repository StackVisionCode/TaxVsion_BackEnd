using Microsoft.Extensions.Hosting;
using TaxVision.Connectors.Application.Abstractions;

namespace TaxVision.Connectors.Tests.Infrastructure;

/// <summary>La app ya arrancó: el job no espera.</summary>
internal sealed class StartedApplicationLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted { get; } = new(canceled: true);
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication() { }
}

/// <summary>Avisa cuando alguien intentó tomar el lock (y con qué resultado).</summary>
internal sealed class SignalingDistributedLock(IDistributedLock inner) : IDistributedLock
{
    public TaskCompletionSource<bool> Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var handle = await inner.AcquireAsync(key, ttl, ct);
        Attempted.TrySetResult(handle.IsAcquired);
        return handle;
    }
}
