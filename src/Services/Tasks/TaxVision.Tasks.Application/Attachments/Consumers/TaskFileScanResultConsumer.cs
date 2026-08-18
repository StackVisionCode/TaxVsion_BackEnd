using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Attachments.Consumers;

/// <summary>
/// Task no escanea nada: reacciona al veredicto de CloudStorage. El exchange trae los archivos de
/// todo el monorepo, así que un <c>fileId</c> que no es de ninguna tarea sale en silencio —tirar
/// excepción llenaría la DLQ de eventos ajenos—.
/// </summary>
public static class TaskFileScanResultConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TaskItem> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt.CorrelationId, evt.EventId)))
        {
            if (await LoadOwnerAsync(evt.TenantId, evt.FileId, tasks, logger, ct) is not { } task)
                return;

            if (task.MarkAttachmentAvailable(evt.FileId))
                await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static async Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TaskItem> logger,
        CancellationToken ct
    ) =>
        await RejectAsync(
            evt.TenantId,
            evt.FileId,
            "infected",
            CorrelationOf(evt.CorrelationId, evt.EventId),
            tasks,
            unitOfWork,
            bus,
            correlation,
            logger,
            ct
        );

    public static async Task Handle(
        FileBlockedByPolicyIntegrationEvent evt,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TaskItem> logger,
        CancellationToken ct
    ) =>
        await RejectAsync(
            evt.TenantId,
            evt.FileId,
            "blocked-by-policy",
            CorrelationOf(evt.CorrelationId, evt.EventId),
            tasks,
            unitOfWork,
            bus,
            correlation,
            logger,
            ct
        );

    /// <summary>
    /// Lo borraron desde CloudStorage. Sin esto la tarea queda mostrando un adjunto que ya no
    /// existe, y el usuario se entera al hacer clic.
    /// </summary>
    public static async Task Handle(
        FileDeletedIntegrationEvent evt,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TaskItem> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt.CorrelationId, evt.EventId)))
        {
            if (await LoadOwnerAsync(evt.TenantId, evt.FileId, tasks, logger, ct) is not { } task)
                return;

            var attachment = task.Attachments.First(a => a.FileId == evt.FileId && a.IsActive);
            if (!task.MarkAttachmentDetached(evt.FileId, DateTime.UtcNow))
                return;

            await bus.PublishAsync(
                AttachmentEvents.Detached(task, attachment, correlation.CorrelationId, deletedAtSource: true)
            );
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static async Task RejectAsync(
        Guid tenantId,
        Guid fileId,
        string reason,
        string correlationId,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TaskItem> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(correlationId))
        {
            if (await LoadOwnerAsync(tenantId, fileId, tasks, logger, ct) is not { } task)
                return;

            var attachment = task.Attachments.First(a => a.FileId == fileId && a.IsActive);
            if (!task.MarkAttachmentRejected(fileId, reason, DateTime.UtcNow))
                return;

            await bus.PublishAsync(AttachmentEvents.Rejected(task, attachment, reason, correlation.CorrelationId));
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// El repositorio busca sin tenant —el consumer no corre en un scope HTTP—, así que el tenant
    /// del evento se compara contra el dueño real antes de tocar nada.
    /// </summary>
    private static async Task<TaskItem?> LoadOwnerAsync(
        Guid tenantId,
        Guid fileId,
        ITaskRepository tasks,
        ILogger<TaskItem> logger,
        CancellationToken ct
    )
    {
        var task = await tasks.GetByAttachmentFileIdAsync(fileId, ct);
        if (task is null)
            return null;

        if (task.TenantId == tenantId)
            return task;

        logger.LogWarning(
            "File {FileId} scan event carries tenant {EventTenantId} but the owning task belongs to {TaskTenantId} — ignored.",
            fileId,
            tenantId,
            task.TenantId
        );
        return null;
    }

    private static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
