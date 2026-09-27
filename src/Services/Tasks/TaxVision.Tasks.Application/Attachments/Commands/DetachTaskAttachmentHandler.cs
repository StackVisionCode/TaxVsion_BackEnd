using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Attachments.Commands;

public sealed record DetachTaskAttachmentCommand(
    Guid TenantId,
    Guid TaskId,
    Guid FileId,
    Guid ByUserId = default,
    bool HasManageAll = false
);

/// <summary>
/// Quita el adjunto de la tarea sin tocar el archivo: CloudStorage es su dueño y otros servicios
/// pueden estar referenciando el mismo id.
/// </summary>
public static class DetachTaskAttachmentHandler
{
    public static async Task<Result> Handle(
        DetachTaskAttachmentCommand command,
        ITaskRepository tasks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdWithAttachmentsAsync(command.TenantId, command.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure(found.Error);

        // A1 — quitarle un adjunto a la tarea de otro es mutarla. El archivo sigue en CloudStorage, pero
        // el colega pierde la referencia sin haber hecho nada.
        if (!TaskAccessPolicy.CanMutate(found.Value, command.ByUserId, command.HasManageAll))
            return Result.Failure(TaskErrors.Forbidden);

        var attachment = found.Value.Attachments.FirstOrDefault(a => a.FileId == command.FileId && a.IsActive);

        var detached = found.Value.DetachFile(command.FileId, DateTime.UtcNow);
        if (detached.IsFailure)
            return detached;

        await bus.PublishAsync(
            AttachmentEvents.Detached(found.Value, attachment!, correlation.CorrelationId, deletedAtSource: false)
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
