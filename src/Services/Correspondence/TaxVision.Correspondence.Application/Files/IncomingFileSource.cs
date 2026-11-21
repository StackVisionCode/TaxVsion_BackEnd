using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Correspondence.Domain.Inbox;
using Wolverine;

namespace TaxVision.Correspondence.Application.Files;

/// <summary>Declaración de los adjuntos entrantes ya guardados en CloudStorage (File Library, origen mail).</summary>
public static class IncomingFileSource
{
    public const string Service = "correspondence";
    public const string ResourceType = "incoming_email";
    public const string Role = "incoming_attachment";

    /// <summary>Re-declara cada adjunto del correo que ya tiene archivo; los no abiertos no existen en CloudStorage.</summary>
    public static async Task PublishAsync(IMessageBus bus, IncomingEmail email, string correlationId)
    {
        foreach (var attachment in email.Attachments)
        {
            if (attachment.CloudStorageFileId is not { } fileId)
                continue;
            await bus.PublishAsync(Declare(email, fileId, correlationId));
        }
    }

    public static FileSourceDeclaredIntegrationEvent Declare(IncomingEmail email, Guid fileId, string correlationId) =>
        new()
        {
            TenantId = email.TenantId,
            CorrelationId = correlationId,
            FileId = fileId,
            SourceService = Service,
            SourceResourceType = ResourceType,
            SourceResourceId = email.Id,
            SourceRole = Role,
            RelatedCustomerId = email.CustomerId,
        };
}
