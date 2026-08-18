using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Timers.Queries;

public sealed record ListTaskTimersQuery(Guid TenantId, Guid TaskId);

public static class ListTaskTimersHandler
{
    public static async Task<Result<IReadOnlyList<TaskTimerResponse>>> Handle(
        ListTaskTimersQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdWithTimersAsync(query.TenantId, query.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<IReadOnlyList<TaskTimerResponse>>(found.Error);

        return Result.Success<IReadOnlyList<TaskTimerResponse>>([.. found.Value.Timers.Select(TaskTimerResponse.From)]);
    }
}
