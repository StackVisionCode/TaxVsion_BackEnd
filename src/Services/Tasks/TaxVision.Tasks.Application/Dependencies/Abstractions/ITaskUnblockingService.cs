using BuildingBlocks.Messaging.TasksIntegrationEvents;

namespace TaxVision.Tasks.Application.Dependencies.Abstractions;

/// <summary>No persiste: muta las sucesoras rastreadas y las guarda el handler que la llamó.</summary>
public interface ITaskUnblockingService
{
    /// <summary>Devuelve los avisos de las sucesoras liberadas: el handler los publica después de guardar.</summary>
    Task<IReadOnlyList<TaskUnblockedIntegrationEvent>> ApplyPredecessorClosedAsync(
        Guid tenantId,
        Guid predecessorTaskId,
        CancellationToken ct = default
    );

    Task ApplyPredecessorReopenedAsync(Guid tenantId, Guid predecessorTaskId, CancellationToken ct = default);
}
