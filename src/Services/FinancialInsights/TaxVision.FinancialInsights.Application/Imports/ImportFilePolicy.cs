namespace TaxVision.FinancialInsights.Application.Imports;

public static class ImportFilePolicy
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/jpeg", "image/png", "text/csv",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    public static bool CanRegister(string? ownerType, Guid? ownerId, Guid customerId,
        string? status, string? detectedContentType, long sizeBytes) =>
        string.Equals(ownerType, "Customer", StringComparison.OrdinalIgnoreCase)
        && ownerId == customerId
        && string.Equals(status, "Available", StringComparison.OrdinalIgnoreCase)
        && detectedContentType is not null && AllowedTypes.Contains(detectedContentType)
        && sizeBytes is > 0 and <= 25 * 1024 * 1024;

    public static bool CanAwaitScan(string? ownerType, Guid? ownerId, Guid customerId,
        string? status, string? declaredContentType, long sizeBytes) =>
        string.Equals(ownerType, "Customer", StringComparison.OrdinalIgnoreCase)
        && ownerId == customerId
        && status is "PendingScan" or "Scanning"
        && declaredContentType is not null && AllowedTypes.Contains(declaredContentType)
        && sizeBytes is > 0 and <= 25 * 1024 * 1024;
}
