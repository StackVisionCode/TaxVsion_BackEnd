namespace TaxVision.CloudStorage.Application.Configuration;

// Tipos de archivo, políticas por carpeta y límites por plan ya no viven aquí: catálogo y carpetas en BD
// (FileTypeDefinition / FolderFilePolicy, PlatformAdmin), plan desde entitlements de Subscription.
// Vigencia de URLs, reserva de subida, papelera y topes ZIP: StoragePlatformSettings (BD, PlatformAdmin).

public sealed class CloudStorageOptions
{
    public const string SectionName = "CloudStorage";

    public string MainBucket { get; set; } = "taxvision-storage";
    public string TempBucket { get; set; } = "taxvision-temp";
    public string QuarantineBucket { get; set; } = "taxvision-quarantine";

    /// <summary>
    /// Cuando true, un archivo guardado por un servicio (M2M, SaveFileRequested) se coloca
    /// automaticamente en su carpeta de sistema segun FolderType (ver SystemFolderCatalog).
    /// Los tipos internos se quedan en raiz. Flag para poder apagarlo en rollout.
    /// </summary>
    public bool AutoSystemFolders { get; set; } = true;

    /// <summary>
    /// CST-15: minutos desde el evento en que una reasignación espera a que el archivo se catalogue. Tiene que
    /// cubrir las esperas de <c>Messaging:DependencyNotReady</c>, para que lo fresco termine en la cola de errores.
    /// </summary>
    public int ReassignOwnerFileWaitMinutes { get; set; } = 60;

    /// <summary>Cuota del tenant cuando su plan aún no trae <c>storage.max_bytes</c>.</summary>
    public long DefaultStorageQuotaBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>
    /// Fase U — tamano de cada parte en un multipart upload. 5MB es el minimo que
    /// exige la API S3 para cualquier parte que no sea la ultima (la ultima puede
    /// ser mas chica) — no bajar de eso o InitiateMultipartUpload/UploadPart fallan.
    /// </summary>
    public long MultipartPartSizeBytes { get; set; } = 5L * 1024 * 1024;

    // Rescate de escaneos atascados (CST-1).
    public bool StuckScanRescueEnabled { get; set; } = true;

    /// <summary>
    /// Escaneos a la vez en esta instancia (CST-16). Cada uno ocupa una conexión de ClamAV, un archivo temporal
    /// y un turno de ContentScanner: no conviene pasar de su <c>MaxConcurrentScans</c>.
    /// </summary>
    public int ScanParallelism { get; set; } = 2;

    /// <summary>Antigüedad mínima en PendingScan/Scanning; mayor que el peor vaciado de la cola.</summary>
    public int StuckScanThresholdMinutes { get; set; } = 120;

    public int StuckScanRescueIntervalMinutes { get; set; } = 15;

    public int StuckScanRescueBatchSize { get; set; } = 100;

    /// <summary>Reencolados antes de marcar el archivo ScanFailed con motivo scan_stalled.</summary>
    public int MaxScanAttempts { get; set; } = 3;

    /// <summary>
    /// D-CST-5/CST-18: días que la regla de lifecycle de cuarentena conserva la evidencia bajo purged/,
    /// contados desde la purga.
    /// </summary>
    public int QuarantinePurgedRetentionDays { get; set; } = 30;
}
