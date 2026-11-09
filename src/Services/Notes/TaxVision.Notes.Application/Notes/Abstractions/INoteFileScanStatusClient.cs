namespace TaxVision.Notes.Application.Notes.Abstractions;

/// <summary>Estado del archivo en CloudStorage, visto desde Notes.</summary>
public enum RemoteFileStatus
{
    /// <summary>Todavía en subida o escaneo: no hay veredicto.</summary>
    Scanning = 0,
    Available = 1,
    Infected = 2,
    BlockedByPolicy = 3,
    ScanFailed = 4,
    PendingReview = 5,

    /// <summary>En la papelera de CloudStorage: puede volver, así que no se decide nada.</summary>
    Deleted = 6,

    /// <summary>CloudStorage respondió que el archivo no existe (purgado o de otro tenant).</summary>
    NotFound = 7,
}

/// <summary>
/// Pregunta a CloudStorage el veredicto de un archivo (M2M, solo metadatos). El veredicto se publica
/// una vez: un adjunto creado después de esa publicación no lo recibe. Un error HTTP se lanza.
/// </summary>
public interface INoteFileScanStatusClient
{
    Task<RemoteFileStatus> GetStatusAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
}
