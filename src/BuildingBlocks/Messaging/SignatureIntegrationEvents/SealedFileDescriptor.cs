namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>Documento sellado disponible para descarga.</summary>
public sealed record SealedFileDescriptor(
    Guid DocumentId,
    Guid SealedFileId,
    string Title,
    string? ShareToken = null
);
