namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

public sealed record PartialCopyFileDescriptor(
    Guid DocumentId,
    Guid PartialCopyFileId,
    string Title,
    string? ShareToken
);
