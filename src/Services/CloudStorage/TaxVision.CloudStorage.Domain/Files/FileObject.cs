using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Files;

public sealed class FileObject : TenantEntity
{
    private FileObject() { }

    public OwnerType OwnerType { get; private set; }
    public Guid? OwnerId { get; private set; }
    public FolderType FolderType { get; private set; }
    public int? TaxYear { get; private set; }

    /// <summary>Fase C2 — carpeta navegable donde vive el archivo (null = raiz). No afecta ObjectKey/MinIO.</summary>
    public Guid? FolderId { get; private set; }

    /// <summary>
    /// Fase C4 — cuando entro a FolderId (null si FolderId es null). Distinto de
    /// CreatedAtUtc: un archivo puede subirse mucho antes de moverse a una
    /// carpeta. FolderShareCoverage lo usa para decidir si un ShareLink de
    /// carpeta con AppliesToFutureItems=false ya lo cubria al crearse el link.
    /// </summary>
    public DateTime? FolderAssignedAtUtc { get; private set; }
    public string ObjectKey { get; private set; } = default!;
    public string OriginalName { get; private set; } = default!;
    public string DeclaredContentType { get; private set; } = default!;
    public string? DetectedContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public string? ChecksumSha256 { get; private set; }
    public FileStatus Status { get; private set; }
    public string? ScanReport { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UploadExpiresAtUtc { get; private set; }
    public DateTime? ScannedAtUtc { get; private set; }
    public DateTime? SoftDeletedAtUtc { get; private set; }
    public DateTime? SoftDeleteExpiresAtUtc { get; private set; }
    public bool IsLegalHeld { get; private set; }

    /// <summary>
    /// Fase U — id de la sesion de multipart upload en S3/MinIO (null para uploads
    /// de un solo POST). Se necesita para poder llamar AbortMultipartUpload si el
    /// cliente nunca completa o si Complete falla: sin este id, un upload multiparte
    /// abandonado deja las partes ya subidas huerfanas en MinIO para siempre (Delete
    /// normal no hace nada porque el objeto ensamblado todavia no existe).
    /// </summary>
    public string? MultipartUploadId { get; private set; }

    public static Result<FileObject> Register(
        Guid id,
        Guid tenantId,
        OwnerType ownerType,
        Guid? ownerId,
        FolderType folderType,
        int? taxYear,
        ObjectKey objectKey,
        string originalName,
        string contentType,
        long sizeBytes,
        Guid createdBy,
        DateTime nowUtc,
        DateTime uploadExpiresAtUtc
    )
    {
        if (sizeBytes <= 0)
            return Result.Failure<FileObject>(FileErrors.InvalidSize);
        if (folderType.RequiresYear() && taxYear is null)
            return Result.Failure<FileObject>(FileErrors.YearRequired);
        if (ownerType != OwnerType.Tenant && ownerId is null)
            return Result.Failure<FileObject>(FileErrors.OwnerRequired);

        var file = new FileObject
        {
            Id = id,
            OwnerType = ownerType,
            OwnerId = ownerId,
            FolderType = folderType,
            TaxYear = taxYear,
            ObjectKey = objectKey.Value,
            OriginalName = originalName,
            DeclaredContentType = contentType,
            SizeBytes = sizeBytes,
            Status = FileStatus.PendingUpload,
            CreatedBy = createdBy,
            CreatedAtUtc = nowUtc,
            UploadExpiresAtUtc = uploadExpiresAtUtc,
        };
        file.SetTenant(tenantId);
        return Result.Success(file);
    }

    /// <summary>Fase U — registra el UploadId devuelto por IMultipartUploadStorage.InitiateAsync, para poder abortarlo despues si nunca se completa.</summary>
    public Result AttachMultipartUpload(string uploadId)
    {
        if (Status != FileStatus.PendingUpload)
            return Result.Failure(FileErrors.InvalidTransition);
        MultipartUploadId = uploadId;
        return Result.Success();
    }

    public Result MarkPendingScan()
    {
        if (Status != FileStatus.PendingUpload)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.PendingScan;
        return Result.Success();
    }

    public Result MarkScanning()
    {
        if (Status == FileStatus.Scanning)
            return Result.Success();
        if (Status is not (FileStatus.PendingScan or FileStatus.ScanFailed))
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.Scanning;
        ScanReport = null;
        return Result.Success();
    }

    public Result RejectUpload(string report, DateTime nowUtc)
    {
        if (Status != FileStatus.PendingUpload)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.ScanFailed;
        ScanReport = report;
        ScannedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result ExpireUpload(DateTime nowUtc)
    {
        if (Status != FileStatus.PendingUpload || UploadExpiresAtUtc > nowUtc)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.ScanFailed;
        ScanReport = "Upload reservation expired before completion.";
        ScannedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkAvailable(ChecksumSha256 checksum, string detectedContentType, DateTime nowUtc)
    {
        if (Status != FileStatus.Scanning)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.Available;
        ChecksumSha256 = checksum.Value;
        DetectedContentType = detectedContentType;
        ScanReport = "Clean";
        ScannedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkInfected(string report, string detectedContentType, DateTime nowUtc)
    {
        if (Status != FileStatus.Scanning)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.Infected;
        DetectedContentType = detectedContentType;
        ScanReport = report;
        ScannedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkScanFailed(string report, DateTime nowUtc)
    {
        if (Status != FileStatus.Scanning)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.ScanFailed;
        ScanReport = report;
        ScannedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkBlockedByPolicy(string report, string detectedContentType, DateTime nowUtc)
    {
        if (Status != FileStatus.Scanning)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.BlockedByPolicy;
        DetectedContentType = detectedContentType;
        ScanReport = report;
        ScannedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result MarkPendingReview(string report, string detectedContentType, DateTime nowUtc)
    {
        if (Status != FileStatus.Scanning)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.PendingReview;
        DetectedContentType = detectedContentType;
        ScanReport = report;
        ScannedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result SoftDelete(DateTime nowUtc, TimeSpan retention)
    {
        if (IsLegalHeld)
            return Result.Failure(FileErrors.LegalHold);
        if (Status != FileStatus.Available)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.SoftDeleted;
        SoftDeletedAtUtc = nowUtc;
        SoftDeleteExpiresAtUtc = nowUtc.Add(retention);
        return Result.Success();
    }

    /// <summary>Fase L1.2 — bloquea purge/hard-delete. Idempotente: fallar si ya esta held evita ocultar un segundo pedido de hold distinto (ver auditoria en el handler).</summary>
    public Result PlaceLegalHold()
    {
        if (IsLegalHeld)
            return Result.Failure(FileErrors.AlreadyLegalHeld);
        IsLegalHeld = true;
        return Result.Success();
    }

    /// <summary>Fase L1.2 — libera el hold. No revierte el Status del archivo (ej. BlockedByPolicy sigue bloqueado hasta un reinstate explicito).</summary>
    public Result LiftLegalHold()
    {
        if (!IsLegalHeld)
            return Result.Failure(FileErrors.NotLegalHeld);
        IsLegalHeld = false;
        return Result.Success();
    }

    /// <summary>
    /// Fase L1.3 — bloqueo por notificacion DMCA. Distinto de MarkBlockedByPolicy
    /// (que solo aplica durante el escaneo inicial): un takedown llega despues,
    /// contra un archivo ya Available y en uso.
    /// </summary>
    public Result BlockForTakedown(DateTime nowUtc)
    {
        if (Status != FileStatus.Available)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.BlockedByPolicy;
        ScanReport = "Blocked by DMCA takedown notice.";
        ScannedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Fase L1.3 — revierte BlockForTakedown cuando el equipo legal reinstala el archivo.</summary>
    public Result ReinstateFromTakedown(DateTime nowUtc)
    {
        if (Status != FileStatus.BlockedByPolicy)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.Available;
        ScanReport = "Reinstated after DMCA counter-notice resolution.";
        ScannedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Fase C2 — mueve el archivo a otra carpeta navegable (o a la raiz con null). La existencia/propiedad de la carpeta ya la valido el handler.</summary>
    public void MoveToFolder(Guid? folderId, DateTime nowUtc)
    {
        FolderId = folderId;
        FolderAssignedAtUtc = folderId is null ? null : nowUtc;
    }

    /// <summary>
    /// Recupera el archivo desde la papelera (Fase C1). No toca la cuota: mientras
    /// estuvo en la papelera el objeto fisico en MinIO nunca se borro, asi que
    /// UsedBytes nunca se libero — restaurar solo revierte el estado.
    /// </summary>
    public Result Restore()
    {
        if (Status != FileStatus.SoftDeleted)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.Available;
        SoftDeletedAtUtc = null;
        SoftDeleteExpiresAtUtc = null;
        return Result.Success();
    }
}
