using TaxVision.Subscription.Application.Catalog.Abstractions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Para las pruebas de handlers de un solo hilo: el candado no es lo que se prueba ahí.</summary>
public sealed class NoOpModuleEditSerializer : IModuleEditSerializer
{
    public Task<IAsyncDisposable> AcquireAsync(string moduleCode, CancellationToken ct) =>
        Task.FromResult<IAsyncDisposable>(new Lease());

    private sealed class Lease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
