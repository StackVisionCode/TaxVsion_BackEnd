namespace TaxVision.Tasks.Application.Dependencies.Abstractions;

/// <summary>No persiste: muta las sucesoras rastreadas y las guarda el handler que la llamó.</summary>
public interface ITaskUnblockingService
{
    Task ApplyPredecessorClosedAsync(Guid tenantId, Guid predecessorTaskId, CancellationToken ct = default);

    Task ApplyPredecessorReopenedAsync(Guid tenantId, Guid predecessorTaskId, CancellationToken ct = default);
}
