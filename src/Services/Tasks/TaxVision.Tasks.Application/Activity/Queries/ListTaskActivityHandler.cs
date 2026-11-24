using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Activity.Abstractions;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Activity;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Activity;

public sealed record ListTaskActivityQuery(
    Guid TenantId,
    Guid TaskId,
    Guid ActorUserId,
    bool CanViewAll,
    DateTime? Before,
    int Take
);

public sealed record TaskActivityResponse(
    Guid Id,
    string Kind,
    Guid? ActorUserId,
    DateTime OccurredAtUtc,
    IReadOnlyDictionary<string, string?> Data
);

/// <summary><c>NextBefore</c> se manda como <c>before</c> para la página siguiente; null = no hay más.</summary>
public sealed record TaskActivityPage(IReadOnlyList<TaskActivityResponse> Items, DateTime? NextBefore);

/// <summary>
/// El historial de una tarea, con la misma visibilidad que la tarea. Mover a otra etapa deja dos eventos con la misma
/// hora (la etapa y el estado de la oficina): se leen como una sola entrada con el nombre del estado.
/// </summary>
public static class ListTaskActivityHandler
{
    public const int MaxTake = 100;
    private const int TieSlack = 20;

    /// <summary>La entrada de etapa que acompaña a un cambio de estado hacia <paramref name="category"/>.</summary>
    private static TaskActivityKind? StageEntryOf(string? category) =>
        category switch
        {
            nameof(TaskItemStatus.NotStarted) => TaskActivityKind.MovedToNotStarted,
            nameof(TaskItemStatus.InProgress) => TaskActivityKind.Started,
            nameof(TaskItemStatus.WaitingOnClient) => TaskActivityKind.WaitingOnClient,
            nameof(TaskItemStatus.Completed) => TaskActivityKind.Completed,
            nameof(TaskItemStatus.Cancelled) => TaskActivityKind.Cancelled,
            _ => null,
        };

    public static async Task<Result<TaskActivityPage>> Handle(
        ListTaskActivityQuery query,
        ITaskRepository tasks,
        ITaskActivityRepository activities,
        IWorkflowStatusRepository statuses,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var task = await tasks.GetByIdForReadAsync(query.TenantId, query.TaskId, assignedTo, ct);
        if (task.IsFailure)
            return Result.Failure<TaskActivityPage>(task.Error);

        var take = Math.Clamp(query.Take, 1, MaxTake);
        var rows = await activities.ListAsync(query.TenantId, query.TaskId, query.Before, take + TieSlack, ct);
        var page = CutKeepingTies(rows, take);
        var names = (await statuses.ListAsync(query.TenantId, includeArchived: true, ct)).ToDictionary(
            s => s.Id.ToString(),
            s => s.DisplayName
        );
        var items = Merge(page).Select(item => WithStatusNames(item, names)).ToList();
        var nextBefore = rows.Count > page.Count ? page[^1].OccurredAtUtc : (DateTime?)null;
        return Result.Success(new TaskActivityPage(items, nextBefore));
    }

    /// <summary>Corta en <paramref name="take"/> sin separar entradas de la misma hora, que se leen juntas.</summary>
    private static List<TaskActivity> CutKeepingTies(IReadOnlyList<TaskActivity> rows, int take)
    {
        if (rows.Count <= take)
            return [.. rows];
        var page = rows.Take(take).ToList();
        foreach (var row in rows.Skip(take))
        {
            if (row.OccurredAtUtc != page[^1].OccurredAtUtc)
                break;
            page.Add(row);
        }
        return page;
    }

    private static IEnumerable<TaskActivityResponse> Merge(List<TaskActivity> page)
    {
        var items = page.Select(a => new TaskActivityResponse(
                a.Id,
                a.Kind.ToString(),
                a.ActorUserId,
                a.OccurredAtUtc,
                TaskActivityData.Read(a.DataJson)
            ))
            .ToList();
        foreach (var change in items.Where(IsStageChange).ToList())
        {
            var stage = items.FirstOrDefault(i =>
                i.OccurredAtUtc == change.OccurredAtUtc && i.Kind == StageEntryOf(change.Data["toCategory"]).ToString()
            );
            if (stage is null)
                continue;
            var data = new Dictionary<string, string?>(stage.Data)
            {
                ["toStatusId"] = change.Data.GetValueOrDefault("toStatusId"),
            };
            items[items.IndexOf(stage)] = stage with { Data = data };
            items.Remove(change);
        }
        return items;
    }

    private static bool IsStageChange(TaskActivityResponse item) =>
        item.Kind == nameof(TaskActivityKind.StatusChanged)
        && item.Data.GetValueOrDefault("toCategory") is { } to
        && item.Data.GetValueOrDefault("fromCategory") != to
        && StageEntryOf(to) is not null;

    private static TaskActivityResponse WithStatusNames(TaskActivityResponse item, Dictionary<string, string> names)
    {
        var data = new Dictionary<string, string?>(item.Data);
        foreach (var (idKey, nameKey) in new[] { ("fromStatusId", "fromStatusName"), ("toStatusId", "toStatusName") })
            if (data.GetValueOrDefault(idKey) is { } id && names.TryGetValue(id, out var name))
                data[nameKey] = name;
        return item with { Data = data };
    }
}
