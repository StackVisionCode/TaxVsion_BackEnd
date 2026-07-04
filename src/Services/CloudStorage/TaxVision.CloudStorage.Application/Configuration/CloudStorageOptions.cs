namespace TaxVision.CloudStorage.Application.Configuration;

public sealed class CloudStorageOptions
{
    public const string SectionName = "CloudStorage";

    public string MainBucket { get; set; } = "taxvision-storage";
    public string TempBucket { get; set; } = "taxvision-temp";
    public string QuarantineBucket { get; set; } = "taxvision-quarantine";
    public int PresignedUrlMinutes { get; set; } = 5;
    public int UploadReservationHours { get; set; } = 24;
    public long DefaultStorageQuotaBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public long DefaultMaxFileSizeBytes { get; set; } = 25L * 1024 * 1024;
    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf",
        ".doc",
        ".docx",
        ".xls",
        ".xlsx",
        ".ppt",
        ".pptx",
        ".txt",
        ".csv",
        ".rtf",
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".webp",
        ".zip",
        ".xml",
        ".json",
    ];
    public string[] AllowedContentTypes { get; set; } =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain",
        "text/csv",
        "application/rtf",
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
        "application/zip",
        "application/xml",
        "text/xml",
        "application/json",
    ];

    public Dictionary<string, StoragePlanPolicy> PlanPolicies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public StoragePlanPolicy ResolvePlanPolicy(string planCode)
    {
        if (PlanPolicies.TryGetValue(planCode, out var configured))
            return configured;
        return new StoragePlanPolicy
        {
            MaxFileSizeBytes = DefaultMaxFileSizeBytes,
            AllowedExtensions = AllowedExtensions,
            AllowedContentTypes = AllowedContentTypes,
        };
    }
}

public sealed class StoragePlanPolicy
{
    public long MaxFileSizeBytes { get; set; }
    public string[] AllowedExtensions { get; set; } = [];
    public string[] AllowedContentTypes { get; set; } = [];
}
