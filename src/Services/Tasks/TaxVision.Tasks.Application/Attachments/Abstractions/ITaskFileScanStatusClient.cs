namespace TaxVision.Tasks.Application.Attachments.Abstractions;

/// <summary>El veredicto que CloudStorage ya emitió sobre un archivo.</summary>
public enum RemoteFileScanStatus
{
    /// <summary>Todavía no se pronunció, o no se pudo preguntar.</summary>
    Unknown = 0,
    Available = 1,
    Infected = 2,
    BlockedByPolicy = 3,
    Deleted = 4,
}

public sealed record RemoteFileMetadata(
    RemoteFileScanStatus Status,
    string? OwnerType,
    Guid? OwnerId,
    string? FolderType,
    string? ContentType,
    long? SizeBytes,
    string? OriginalName
)
{
    public static RemoteFileMetadata Unknown { get; } =
        new(RemoteFileScanStatus.Unknown, null, null, null, null, null, null);
}

/// <summary>
/// Sólo metadatos: el estado del escaneo, nunca el byte. Existe porque el veredicto se publica una
/// vez y no se republica; un adjunto creado después de esa publicación se quedaría esperando para
/// siempre si nadie va a preguntar.
/// </summary>
public interface ITaskFileScanStatusClient
{
    Task<RemoteFileScanStatus> GetStatusAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
    Task<RemoteFileMetadata> GetFileMetadataAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
}
