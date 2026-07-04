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

    public Result SoftDelete(DateTime nowUtc, TimeSpan retention)
    {
        if (IsLegalHeld)
            return Result.Failure(new Error("File.LegalHold", "The file is under legal hold."));
        if (Status != FileStatus.Available)
            return Result.Failure(FileErrors.InvalidTransition);
        Status = FileStatus.SoftDeleted;
        SoftDeletedAtUtc = nowUtc;
        SoftDeleteExpiresAtUtc = nowUtc.Add(retention);
        return Result.Success();
    }
}
