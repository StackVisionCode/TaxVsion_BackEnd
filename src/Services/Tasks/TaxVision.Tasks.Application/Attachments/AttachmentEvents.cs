using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Attachments;

/// <summary>Arma los contratos de adjunto en un solo sitio: los publican cuatro caminos distintos.</summary>
internal static class AttachmentEvents
{
    public static TaskAttachmentAddedIntegrationEvent Added(
        TaskItem task,
        TaskAttachment attachment,
        string correlationId
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            AttachmentId = attachment.Id,
            FileId = attachment.FileId,
            DisplayName = attachment.DisplayName,
            Origin = attachment.Origin.ToString(),
            Status = attachment.Status.ToString(),
            AttachedByUserId = attachment.AttachedByUserId,
        };

    public static TaskAttachmentDetachedIntegrationEvent Detached(
        TaskItem task,
        TaskAttachment attachment,
        string correlationId,
        bool deletedAtSource
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            AttachmentId = attachment.Id,
            FileId = attachment.FileId,
            DeletedAtSource = deletedAtSource,
        };

    public static TaskAttachmentRejectedIntegrationEvent Rejected(
        TaskItem task,
        TaskAttachment attachment,
        string reason,
        string correlationId
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            TaskId = task.Id,
            TaskTitle = task.Title.Value,
            AttachmentId = attachment.Id,
            FileId = attachment.FileId,
            DisplayName = attachment.DisplayName,
            Reason = reason,
            AttachedByUserId = attachment.AttachedByUserId,
        };
}
