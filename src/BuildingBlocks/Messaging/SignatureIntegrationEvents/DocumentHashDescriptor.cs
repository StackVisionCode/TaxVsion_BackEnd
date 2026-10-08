namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>Snapshot de integridad de un documento original dentro de una solicitud.</summary>
public sealed record DocumentHashDescriptor(Guid DocumentId, Guid OriginalFileId, string HashSha256);
