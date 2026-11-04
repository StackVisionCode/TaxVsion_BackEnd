using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Application.Permissions.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.ClientRequests;

namespace TaxVision.Tasks.Application.ClientRequests.Consumers;

/// <summary>
/// Al retirar a un empleado, los pedidos al cliente ABIERTOS cuyos avisos iban a él pasan a otra persona:
/// el asignado de la tarea vinculada si no es el retirado; si no, el sucesor; si no, quien ejecutó el retiro.
/// Solo cuenta quien esté activo en la proyección local (fail-closed). No asume orden con la reasignación de
/// tareas: un asignado que todavía es el retirado se salta. <c>RequestedByUserId</c> no cambia (procedencia).
/// Idempotente: lo traspasado sale del filtro.
/// </summary>
public static class ClientRequestsUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        IClientRequestRepository requests,
        ITaskRepository tasks,
        IUserPermissionsProjectionRepository users,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<ClientRequest> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        var open = await requests.ListOpenForStaffRecipientAsync(msg.TenantId, msg.UserId, ct);
        if (open.Count == 0)
            return;

        var handedOver = 0;
        foreach (var request in open)
            handedOver += await HandOverAsync(msg, request, tasks, users, ct) ? 1 : 0;

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Offboarded user {UserId}: {HandedOver} of {Total} open client request(s) handed over in tenant {TenantId}.",
            msg.UserId,
            handedOver,
            open.Count,
            msg.TenantId
        );
    }

    private static async Task<bool> HandOverAsync(
        UserOffboardedIntegrationEvent msg,
        ClientRequest request,
        ITaskRepository tasks,
        IUserPermissionsProjectionRepository users,
        CancellationToken ct
    )
    {
        var taskAssignee = await TaskAssigneeOfAsync(request, tasks, ct);
        foreach (var candidate in new[] { taskAssignee, msg.SuccessorUserId, msg.OffboardedByUserId })
        {
            if (candidate is not { } userId || userId == msg.UserId || userId == Guid.Empty)
                continue;
            if (await users.GetAsync(msg.TenantId, userId, ct) is not { IsActive: true })
                continue;
            return request.HandOverFollowUp(userId).IsSuccess;
        }

        return false;
    }

    private static async Task<Guid?> TaskAssigneeOfAsync(
        ClientRequest request,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        if (request.TaskId is not { } taskId)
            return null;
        var task = await tasks.GetByIdAsync(request.TenantId, taskId, ct);
        return task.IsSuccess ? task.Value.AssigneeUserId : null;
    }
}
