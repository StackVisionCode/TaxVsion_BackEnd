using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files;

public sealed record InitiateUploadRequest(
    string OriginalName,
    string ContentType,
    long SizeBytes,
    OwnerType OwnerType,
    Guid? OwnerId,
    FolderType FolderType,
    int? TaxYear
);

public sealed record InitiatedUploadResponse(
    Guid FileId,
    Uri UploadUrl,
    IReadOnlyDictionary<string, string> FormData,
    DateTime ExpiresAtUtc,
    FileStatus Status
);

public sealed record FileResponse(
    Guid Id,
    OwnerType OwnerType,
    Guid? OwnerId,
    FolderType FolderType,
    int? TaxYear,
    string OriginalName,
    string DeclaredContentType,
    string? DetectedContentType,
    long SizeBytes,
    string? ChecksumSha256,
    FileStatus Status,
    string? ScanReport,
    DateTime CreatedAtUtc,
    DateTime? ScannedAtUtc
);

public sealed record DownloadUrlResponse(Guid FileId, Uri DownloadUrl, DateTime ExpiresAtUtc);

internal static class FileResponseMapper
{
    public static FileResponse Map(FileObject file) =>
        new(
            file.Id,
            file.OwnerType,
            file.OwnerId,
            file.FolderType,
            file.TaxYear,
            file.OriginalName,
            file.DeclaredContentType,
            file.DetectedContentType,
            file.SizeBytes,
            file.ChecksumSha256,
            file.Status,
            file.ScanReport,
            file.CreatedAtUtc,
            file.ScannedAtUtc
        );
}

internal static class FileTypeCompatibility
{
    public static bool Matches(string originalName, string detectedContentType)
    {
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => detectedContentType == "application/pdf",
            ".doc" => detectedContentType == "application/msword",
            ".docx" => detectedContentType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => detectedContentType == "application/vnd.ms-excel",
            ".xlsx" => detectedContentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => detectedContentType == "application/vnd.ms-powerpoint",
            ".pptx" => detectedContentType
                == "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => detectedContentType == "text/plain",
            ".csv" => detectedContentType == "text/csv",
            ".rtf" => detectedContentType == "application/rtf",
            ".jpg" or ".jpeg" => detectedContentType == "image/jpeg",
            ".png" => detectedContentType == "image/png",
            ".gif" => detectedContentType == "image/gif",
            ".webp" => detectedContentType == "image/webp",
            ".zip" => detectedContentType == "application/zip",
            ".xml" => detectedContentType is "application/xml" or "text/xml",
            ".json" => detectedContentType == "application/json",
            // El HTML es texto plano sin magic bytes fuertes: los detectores suelen devolver text/plain.
            ".html" => detectedContentType is "text/html" or "text/plain",
            _ => false,
        };
    }
}
