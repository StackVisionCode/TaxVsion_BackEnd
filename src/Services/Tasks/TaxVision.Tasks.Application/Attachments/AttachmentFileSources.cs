using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Attachments;

/// <summary>Declaración a CloudStorage del archivo que se subió a una tarea: así entra en la biblioteca.</summary>
public static class AttachmentFileSources
{
    public const string Service = "tasks";
    public const string ResourceType = "task";
    public const string Role = "attachment";

    /// <summary>Solo el subido; un enlazado o de plantilla conserva el origen donde nació.</summary>
    public static FileSourceDeclaredIntegrationEvent? ForUploaded(
        TaskItem task,
        TaskAttachment attachment,
        string correlationId
    ) =>
        attachment.Origin == AttachmentOrigin.Uploaded
            ? Declare(task.TenantId, task.Id, task.Reference.CustomerId, attachment.FileId, correlationId)
            : null;

    public static FileSourceDeclaredIntegrationEvent Declare(
        Guid tenantId,
        Guid taskId,
        Guid? customerId,
        Guid fileId,
        string correlationId
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            FileId = fileId,
            SourceService = Service,
            SourceResourceType = ResourceType,
            SourceResourceId = taskId,
            SourceRole = Role,
            RelatedCustomerId = customerId,
        };
}
