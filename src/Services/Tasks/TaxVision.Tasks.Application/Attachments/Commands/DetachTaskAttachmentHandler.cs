using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using Wolverine;

namespace TaxVision.Tasks.Application.Attachments.Commands;

public sealed record DetachTaskAttachmentCommand(Guid TenantId, Guid TaskId, Guid FileId);

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
