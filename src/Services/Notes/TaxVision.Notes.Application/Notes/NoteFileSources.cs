using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes;

/// <summary>Declaración a CloudStorage del archivo adjunto a una nota: así entra en la biblioteca.</summary>
public static class NoteFileSources
{
    public const string Service = "notes";
    public const string ResourceType = "note";
    public const string Role = "attachment";

    public static FileSourceDeclaredIntegrationEvent ForAttachment(Note note, Guid fileId, string correlationId) =>
        Declare(note.TenantId, note.Id, note.Reference.TargetType, note.Reference.TargetId, fileId, correlationId);

    // El cliente relacionado solo existe si la nota es de un cliente.
    public static FileSourceDeclaredIntegrationEvent Declare(
        Guid tenantId,
        Guid noteId,
        NoteTargetType targetType,
        Guid? targetId,
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
            SourceResourceId = noteId,
            SourceRole = Role,
            RelatedCustomerId = CustomerOf(targetType, targetId),
        };

    public static Guid? CustomerOf(NoteTargetType targetType, Guid? targetId) =>
        targetType == NoteTargetType.Customer ? targetId : null;
}
