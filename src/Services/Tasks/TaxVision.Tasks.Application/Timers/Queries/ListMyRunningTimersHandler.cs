using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Timers.Queries;

public sealed record ListMyRunningTimersQuery(Guid TenantId, Guid UserId);

/// <summary>Un temporizador mío en marcha, para avisar al iniciar otro (D-22).</summary>
public sealed record RunningTimerResponse(Guid TaskId, string Title, Guid TimerId, DateTime StartedAtUtc);

public static class ListMyRunningTimersHandler
{
    public static async Task<IReadOnlyList<RunningTimerResponse>> Handle(
        ListMyRunningTimersQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var withRunning = await tasks.ListWithRunningTimersOfAsync(query.TenantId, query.UserId, ct);
        return
        [
            .. withRunning.SelectMany(task =>
                task.Timers.Where(t => t.UserId == query.UserId && t.IsRunning)
                    .Select(t => new RunningTimerResponse(task.Id, task.Title.Value, t.Id, t.StartedAtUtc))
            ),
        ];
    }
}
