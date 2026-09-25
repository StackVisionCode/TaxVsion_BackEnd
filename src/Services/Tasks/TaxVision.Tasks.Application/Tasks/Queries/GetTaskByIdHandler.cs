using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

// ActorUserId + CanViewAll: visibilidad por asignación (P2) — NotFound si la tarea es de un cliente no asignado
// al actor que no ve todo (sin cliente, o asignada/creada por él, sigue visible). No filtrar existencia.
public sealed record GetTaskByIdQuery(Guid TenantId, Guid TaskId, Guid ActorUserId, bool CanViewAll);

/// <summary>
/// Ver las tareas de la firma es parte de <c>tasks.read</c>; lo que exige ser dueño o supervisor es moverlas.
/// P2: la visibilidad por asignación acota qué tareas de CLIENTE puede ver un empleado.
/// </summary>
public static class GetTaskByIdHandler
{
    public static async Task<Result<TaskResponse>> Handle(
        GetTaskByIdQuery query,
        ITaskRepository tasks,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var found = await tasks.GetByIdForReadAsync(query.TenantId, query.TaskId, assignedTo, ct);
        return found.IsFailure
            ? Result.Failure<TaskResponse>(found.Error)
            : Result.Success(TaskResponse.From(found.Value));
    }
}
