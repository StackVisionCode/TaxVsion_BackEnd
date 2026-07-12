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

    public void MarkDeleted()
    {
        Status = FileScanStatus.Deleted;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string TruncateReport(string report) =>
        string.IsNullOrWhiteSpace(report) ? string.Empty
        : report.Length > 1000 ? report[..1000]
        : report;
}
