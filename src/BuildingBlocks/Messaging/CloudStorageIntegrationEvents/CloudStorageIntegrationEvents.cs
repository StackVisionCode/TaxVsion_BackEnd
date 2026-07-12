namespace BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

public sealed record FileAvailableIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required string ObjectKey { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public required string ChecksumSha256 { get; init; }
}

public sealed record FileInfectedDetectedIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required string ObjectKey { get; init; }
    public required string ScanReport { get; init; }
}

/// <summary>
/// Publicado por IContentScanner (moderacion de contenido, no antivirus)
/// cuando el verdict es PolicyViolation. Objeto ya movido a QuarantineBucket.
/// </summary>
public sealed record FileBlockedByPolicyIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required string ObjectKey { get; init; }
    public required string PolicyReason { get; init; }
}

/// <summary>
/// Publicado cuando IContentScanner devuelve Uncertain — requiere revision
/// humana. No hay flujo de reviewer en el MVP (NoOpContentScanner nunca lo
/// dispara); el evento existe para que Notification pueda alertar a
/// compliance en cuanto exista un scanner real.
/// </summary>
public sealed record FilePendingReviewIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required string ObjectKey { get; init; }
    public required string Reason { get; init; }
}

public sealed record FileDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
}

public sealed record StorageLimitExceededIntegrationEvent : IntegrationEvent
{
    public required long AttemptedFileSizeBytes { get; init; }
    public required long UsedBytes { get; init; }
    public required long ReservedBytes { get; init; }
    public required long MaxBytes { get; init; }
}

public sealed record FileAccessAuditedIntegrationEvent : IntegrationEvent
{
    public required Guid FileId { get; init; }
    public required Guid ActorId { get; init; }
    public required string Action { get; init; }
    public required string Outcome { get; init; }
    public string? IpAddress { get; init; }
}
