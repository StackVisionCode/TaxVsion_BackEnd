namespace TaxVision.Subscription.Application.Catalog.Abstractions;

public interface IModuleEditSerializer
{
    Task<IAsyncDisposable> AcquireAsync(string moduleCode, CancellationToken ct);
}
