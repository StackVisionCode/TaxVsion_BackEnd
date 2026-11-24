using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Dependencies.Queries;

public sealed record GetTaskRelationsQuery(Guid TenantId, Guid TaskId, Guid ActorUserId, bool CanViewAll);

/// <param name="WaitingOn">Las tareas que esta espera.</param>
/// <param name="Blocking">Las tareas que esperan a esta.</param>
public sealed record TaskRelationsResponse(
    Guid TaskId,
    IReadOnlyList<TaskRelationResponse> WaitingOn,
    IReadOnlyList<TaskRelationResponse> Blocking
);

/// <param name="DueOn">Fecha civil; las tareas antiguas solo tienen <c>DueAtUtc</c>.</param>
public sealed record TaskRelationResponse(
    Guid TaskId,
    string Title,
    TaskItemStatus Status,
    Guid? AssigneeUserId,
    DateOnly? DueOn,
    DateTime? DueAtUtc,
    Guid? CustomerId
);

/// <summary>
/// Sustituye al grafo más una petición por tarea. Aplica la misma visibilidad que el detalle: una tarea
/// relacionada que el actor no puede ver no sale.
/// </summary>
public static class GetTaskRelationsHandler
{
    public static async Task<Result<TaskRelationsResponse>> Handle(
        GetTaskRelationsQuery query,
        ITaskRepository tasks,
        ITaskDependencyRepository dependencies,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var found = await tasks.GetByIdForReadAsync(query.TenantId, query.TaskId, assignedTo, ct);
        if (found.IsFailure)
            return Result.Failure<TaskRelationsResponse>(found.Error);

        var edges = await dependencies.ListTouchingAsync(query.TenantId, [query.TaskId], ct);
        var waitingOnIds = edges.Where(e => e.TaskId == query.TaskId).Select(e => e.DependsOnTaskId).ToList();
        var blockingIds = edges.Where(e => e.DependsOnTaskId == query.TaskId).Select(e => e.TaskId).ToList();
        var related = await tasks.ListByIdsForReadAsync(
            query.TenantId,
            [.. waitingOnIds, .. blockingIds],
            assignedTo,
            ct
        );

        return Result.Success(
            new TaskRelationsResponse(query.TaskId, Pick(related, waitingOnIds), Pick(related, blockingIds))
        );
    }

    private static List<TaskRelationResponse> Pick(IReadOnlyList<TaskItem> related, List<Guid> ids) =>
        [
            .. related
                .Where(t => ids.Contains(t.Id))
                .OrderBy(t => t.Title.Value)
                .Select(t => new TaskRelationResponse(
                    t.Id,
                    t.Title.Value,
                    t.Status,
                    t.AssigneeUserId,
                    t.Due?.DueOn,
                    t.Due?.DueAtUtc,
                    t.Reference.CustomerId
                )),
        ];
}
