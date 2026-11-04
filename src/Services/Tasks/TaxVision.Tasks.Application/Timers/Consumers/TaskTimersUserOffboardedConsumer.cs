using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Timers.Consumers;

/// <summary>
/// Al retirar a un empleado se cierran por sistema sus timers en marcha (solo él podía pararlos), imputando
/// el tramo a <c>ActualHours</c> igual que un stop manual. El cierre usa la hora del retiro, no la de
/// llegada del evento, para no imputar horas que ya no trabajó. Idempotente: lo cerrado sale del filtro.
/// </summary>
public static class TaskTimersUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TaskTimer> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        var affected = await tasks.ListWithRunningTimersOfAsync(msg.TenantId, msg.UserId, ct);
        if (affected.Count == 0)
            return;

        var stoppedAt = StopTimeOf(msg);
        var stopped = affected.Sum(task => task.StopRunningTimersOf(msg.UserId, stoppedAt));
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "Offboarded user {UserId}: {Count} running timer(s) stopped by the system in tenant {TenantId}.",
            msg.UserId,
            stopped,
            msg.TenantId
        );
    }

    // La hora del retiro, acotada al presente por si llega con reloj adelantado.
    private static DateTime StopTimeOf(UserOffboardedIntegrationEvent msg)
    {
        var now = DateTime.UtcNow;
        return msg.RemovedAtUtc == default || msg.RemovedAtUtc > now ? now : msg.RemovedAtUtc;
    }
}
