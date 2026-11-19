using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

[MessageIdentity("cloudstorage.customer_document_uploaded.v1")]
public sealed record CustomerDocumentUploadedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid FileId { get; init; }
    public Guid? FolderId { get; init; }

    /// <summary>Quién lo subió (aditivo, Work Profile) y su tipo de actor: nombre del enum <c>ActorType</c>.</summary>
    public Guid? UploadedByUserId { get; init; }
    public string? UploadedByActorType { get; init; }
}
