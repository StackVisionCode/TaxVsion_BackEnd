using TaxVision.Tasks.Domain.Activity;

namespace TaxVision.Tasks.Application.Activity.Abstractions;

public interface ITaskActivityRepository
{
    void Add(TaskActivity activity);

    /// <summary>De la más nueva a la más vieja; con <paramref name="before"/>, solo las anteriores a ese instante.</summary>
    Task<IReadOnlyList<TaskActivity>> ListAsync(
        Guid tenantId,
        Guid taskId,
        DateTime? before,
        int take,
        CancellationToken ct = default
    );
}
