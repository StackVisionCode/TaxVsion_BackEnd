using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record GetTaskByIdQuery(Guid TenantId, Guid TaskId);

/// <summary>
/// Sin filtro de propiedad: ver las tareas de la firma es parte de <c>tasks.read</c>. Lo que exige
/// ser dueño o supervisor es moverlas.
/// </summary>
public static class GetTaskByIdHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        GetTaskByIdQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdAsync(query.TenantId, query.TaskId, ct);
        return found.IsFailure
            ? Result.Failure<TaskResponse>(found.Error)
            : Result.Success(TaskResponse.From(found.Value));
    }
}
