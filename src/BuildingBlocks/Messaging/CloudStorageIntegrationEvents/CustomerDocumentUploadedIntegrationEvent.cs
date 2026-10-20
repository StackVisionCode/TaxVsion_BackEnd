using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

[MessageIdentity("cloudstorage.customer_document_uploaded.v1")]
public sealed record CustomerDocumentUploadedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid FileId { get; init; }
    public Guid? FolderId { get; init; }
}
