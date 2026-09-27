using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Attachments.Commands;

public sealed record LinkTaskAttachmentCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid TaskId,
    Guid FileId,
    string? DisplayName,
    string? ContentType,
    long SizeBytes,
    bool HasManageAll = false
);

/// <summary>
/// Enlaza un archivo que ya vive en CloudStorage: el W-2 que el cliente subió por el portal antes
/// de que existiera la tarea. Nace disponible, sin esperar un escaneo que ya ocurrió.
/// </summary>
public static class LinkTaskAttachmentHandler
{
    public static async Task<Result<TaskAttachmentResponse>> Handle(
        LinkTaskAttachmentCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdWithAttachmentsAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<TaskAttachmentResponse>(found.Error);

        // A1 — adjuntar a la tarea de otro es mutarla: el adjunto queda visible para quien la tenga
        // asignada y cuenta contra su límite. Antes alcanzaba con tasks.write.
        if (!TaskAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskAttachmentResponse>(TaskErrors.Forbidden);

        var linked = found.Value.LinkExistingFile(
            command.FileId,
            command.DisplayName,
            command.ContentType,
            command.SizeBytes,
            command.ByUserId,
            DateTime.UtcNow
        );
        if (linked.IsFailure)
            return Result.Failure<TaskAttachmentResponse>(linked.Error);

        await bus.PublishAsync(AttachmentEvents.Added(found.Value, linked.Value, correlation.CorrelationId));
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(TaskAttachmentResponse.From(linked.Value));
    }
}
