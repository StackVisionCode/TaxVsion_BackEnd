namespace TaxVision.Auth.Application.SlotGating.Abstractions;

public interface ISeatSlotRecomputeSerializer
{
    Task<IAsyncDisposable> AcquireAsync(Guid tenantId, CancellationToken ct);
}
