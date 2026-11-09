using BuildingBlocks.Persistence;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Guardado que falla como un timeout de SQL: lo que sí reintenta en línea por InvokeAsync.</summary>
public sealed class FailingUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        throw new TimeoutException("Simulated SQL timeout.");
}
