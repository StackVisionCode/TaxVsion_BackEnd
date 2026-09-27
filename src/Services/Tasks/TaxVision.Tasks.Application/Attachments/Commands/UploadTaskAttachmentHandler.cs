using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Attachments.Commands;

public sealed record UploadTaskAttachmentCommand(
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
/// El frontend ya subió el archivo a CloudStorage con su propio token y trae el id: Task registra
/// la referencia y espera el veredicto del escaneo. Aquí nunca pasa un byte.
/// </summary>
public static class UploadTaskAttachmentHandler
{
    public static async Task<Result<TaskAttachmentResponse>> Handle(
        UploadTaskAttachmentCommand command,
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

        // A1 — mismo criterio que al enlazar un archivo existente.
        if (!TaskAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure<TaskAttachmentResponse>(TaskErrors.Forbidden);

        var attached = found.Value.AttachUploadedFile(
            command.FileId,
            command.DisplayName,
            command.ContentType,
            command.SizeBytes,
            command.ByUserId,
            DateTime.UtcNow
        );
        if (attached.IsFailure)
            return Result.Failure<TaskAttachmentResponse>(attached.Error);

        await bus.PublishAsync(AttachmentEvents.Added(found.Value, attached.Value, correlation.CorrelationId));
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(TaskAttachmentResponse.From(attached.Value));
    }
}
