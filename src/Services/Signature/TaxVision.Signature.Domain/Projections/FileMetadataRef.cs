namespace TaxVision.Signature.Domain.Projections;

/// <summary>
/// Estado del scan/moderación de un archivo de CloudStorage.
/// </summary>
public enum FileScanStatus
{
    Pending,
    Available,
    Infected,
    Deleted,

    /// <summary>CloudStorage no lo aceptó (guardado rechazado, bloqueado por política o scan fallido): nunca estará disponible.</summary>
    Rejected,
}

/// <summary>
/// Proyección local de metadatos de archivos de CloudStorage. Signature no llama a
/// CloudStorage sincrónicamente para saber si un archivo está listo — consulta esta
/// proyección alimentada por eventos.
///
/// <para>
/// Se usa en el consumer de <c>FileAvailableIntegrationEvent</c> para transicionar
/// <c>SignatureRequest.Draft → Ready</c> con el hash pre-firma (regla del diseño §20.5).
/// </para>
/// </summary>
public sealed class FileMetadataRef
{
    private FileMetadataRef() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public string ObjectKey { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public string? ChecksumSha256 { get; private set; }
    public FileScanStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>El archivo ya tiene un resultado final (disponible o no); <c>Pending</c> sigue esperando.</summary>
    public bool HasScanOutcome => Status != FileScanStatus.Pending;

    public static FileMetadataRef ForAvailable(
        Guid tenantId,
        Guid fileId,
        string objectKey,
        string contentType,
        long sizeBytes,
        string checksumSha256
    )
    {
        var now = DateTime.UtcNow;
        return new FileMetadataRef
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileId = fileId,
            ObjectKey = objectKey,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            ChecksumSha256 = checksumSha256,
            Status = FileScanStatus.Available,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    /// <summary>Infectado sin haber pasado por Available (el evento llegó antes que cualquier otro).</summary>
    public static FileMetadataRef ForInfected(Guid tenantId, Guid fileId, string scanReport) =>
        ForOutcome(tenantId, fileId, FileScanStatus.Infected, scanReport);

    /// <summary>Rechazado por CloudStorage sin haber existido nunca como Available.</summary>
    public static FileMetadataRef ForRejected(Guid tenantId, Guid fileId, string reason) =>
        ForOutcome(tenantId, fileId, FileScanStatus.Rejected, reason);

    public void MarkAvailable(string objectKey, string contentType, long sizeBytes, string checksumSha256)
    {
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        ChecksumSha256 = checksumSha256;
        Status = FileScanStatus.Available;
        RejectionReason = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkInfected(string scanReport)
    {
        Status = FileScanStatus.Infected;
        RejectionReason = TruncateReport(scanReport);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Rechazo de CloudStorage. No pisa un resultado ya conocido (Available, Infected o Deleted): un
    /// evento tardío o ajeno no degrada un archivo que sí existe ni cambia un motivo más específico.
    /// </summary>
    public void MarkRejected(string reason)
    {
        if (Status is FileScanStatus.Available or FileScanStatus.Infected or FileScanStatus.Deleted)
            return;

        Status = FileScanStatus.Rejected;
        RejectionReason = TruncateReport(reason);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkDeleted()
    {
        Status = FileScanStatus.Deleted;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    // Sin objeto ni metadatos (nunca hubo FileAvailable): columnas requeridas vacías.
    private static FileMetadataRef ForOutcome(Guid tenantId, Guid fileId, FileScanStatus status, string reason)
    {
        var now = DateTime.UtcNow;
        return new FileMetadataRef
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileId = fileId,
            ObjectKey = string.Empty,
            ContentType = string.Empty,
            SizeBytes = 0,
            Status = status,
            RejectionReason = TruncateReport(reason),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static string TruncateReport(string report) =>
        string.IsNullOrWhiteSpace(report) ? string.Empty
        : report.Length > 1000 ? report[..1000]
        : report;
}
