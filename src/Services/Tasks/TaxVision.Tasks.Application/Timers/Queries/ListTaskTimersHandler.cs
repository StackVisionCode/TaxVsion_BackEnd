using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;

namespace TaxVision.Tasks.Application.Timers.Queries;

public sealed record ListTaskTimersQuery(Guid TenantId, Guid TaskId, Guid ActorUserId, bool CanViewAll);

public static class ListTaskTimersHandler
{
    public static async Task<Result<IReadOnlyList<TaskTimerResponse>>> Handle(
        ListTaskTimersQuery query,
        ITaskRepository tasks,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var viewer = TaskReadGate.ViewerOf(visibility, query.ActorUserId, query.CanViewAll);
        var visible = await TaskReadGate.EnsureVisibleAsync(tasks, query.TenantId, query.TaskId, viewer, ct);
        if (visible.IsFailure)
            return Result.Failure<IReadOnlyList<TaskTimerResponse>>(visible.Error);

        var found = await tasks.GetByIdWithTimersAsync(query.TenantId, query.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<IReadOnlyList<TaskTimerResponse>>(found.Error);

        return Result.Success<IReadOnlyList<TaskTimerResponse>>([.. found.Value.Timers.Select(TaskTimerResponse.From)]);
    }
}
