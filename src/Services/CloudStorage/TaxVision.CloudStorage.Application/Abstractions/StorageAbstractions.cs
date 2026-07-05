using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Abstractions;

public interface IFileObjectRepository
{
    void Add(FileObject file);
    Task<FileObject?> GetAsync(Guid tenantId, Guid fileId, CancellationToken ct);
    Task<IReadOnlyList<FileObject>> ListAsync(
        Guid tenantId,
        Guid? restrictedCustomerId,
        int skip,
        int take,
        CancellationToken ct
    );
    Task<IReadOnlyList<FileObject>> ListExpiredUploadsAsync(DateTime nowUtc, int take, CancellationToken ct);
}

public interface IStorageLimitRepository
{
    void Add(TenantStorageLimit limit);
    Task<TenantStorageLimit?> GetAsync(Guid tenantId, CancellationToken ct);
}

public interface IStorageAuditRepository
{
    void Add(StorageAccessLog log);
    Task<IReadOnlyList<StorageAccessLog>> ListAsync(Guid tenantId, int skip, int take, CancellationToken ct);
}

public interface IObjectStorage
{
    Task<PresignedUpload> CreateUploadPolicyAsync(
        string bucket,
        string objectKey,
        string contentType,
        long exactSizeBytes,
        TimeSpan lifetime,
        CancellationToken ct
    );
    Task<Uri> PresignGetAsync(string bucket, string objectKey, TimeSpan lifetime, CancellationToken ct);
    Task<long> GetSizeAsync(string bucket, string objectKey, CancellationToken ct);
    Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken ct);
    Task DownloadAsync(string bucket, string objectKey, Stream destination, CancellationToken ct);
    Task CopyAsync(string sourceBucket, string objectKey, string destinationBucket, CancellationToken ct);
    Task DeleteAsync(string bucket, string objectKey, CancellationToken ct);
}

public sealed record PresignedUpload(Uri Url, IReadOnlyDictionary<string, string> FormData);

public interface IVirusScanner
{
    Task<VirusScanResult> ScanAsync(Stream content, CancellationToken ct);
}

public sealed record VirusScanResult(VirusScanVerdict Verdict, string Report)
{
    public static VirusScanResult Clean(string report = "Clean") => new(VirusScanVerdict.Clean, report);

    public static VirusScanResult Infected(string report) => new(VirusScanVerdict.Infected, report);

    public static VirusScanResult Error(string report) => new(VirusScanVerdict.Error, report);
}

public enum VirusScanVerdict
{
    Clean,
    Infected,
    Error,
}

public interface IFileContentInspector
{
    Task<InspectedContent> InspectAsync(Stream content, string originalName, CancellationToken ct);
}

public sealed record InspectedContent(
    string ContentType,
    string Sha256,
    bool IsSafe = true,
    string? RejectionReason = null
);

public interface IObjectKeyBuilder
{
    BuildingBlocks.Results.Result<ObjectKey> Build(
        Guid fileId,
        Guid tenantId,
        OwnerType ownerType,
        Guid? ownerId,
        FolderType folderType,
        int? taxYear,
        string originalName
    );
}

public interface ISystemClock
{
    DateTime UtcNow { get; }
}

public sealed record RequestAuditContext(string? IpAddress, string? UserAgent, string CorrelationId);

public sealed record StorageActorScope(bool IsCustomerPortal, Guid? CustomerId)
{
    public bool CanCreate(OwnerType ownerType, Guid? ownerId) =>
        !IsCustomerPortal || (CustomerId.HasValue && ownerType == OwnerType.Customer && ownerId == CustomerId);

    public bool CanAccess(FileObject file) =>
        !IsCustomerPortal
        || (CustomerId.HasValue && file.OwnerType == OwnerType.Customer && file.OwnerId == CustomerId);
}
