using System.Text;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

public sealed class DefaultObjectKeyBuilder : IObjectKeyBuilder
{
    public Result<ObjectKey> Build(
        Guid fileId,
        Guid tenantId,
        OwnerType ownerType,
        Guid? ownerId,
        FolderType folderType,
        int? taxYear,
        string originalName
    )
    {
        if (folderType.RequiresYear() && taxYear is null)
            return Result.Failure<ObjectKey>(FileErrors.YearRequired);
        if (ownerType != OwnerType.Tenant && ownerId is null)
            return Result.Failure<ObjectKey>(FileErrors.OwnerRequired);

        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        var owner = ownerType == OwnerType.Tenant ? "tenant" : $"{ownerType.ToSegment()}/{ownerId:N}";
        var year = folderType.RequiresYear() ? $"/{taxYear}" : string.Empty;
        var key =
            $"tenants/{tenantId:N}/{owner}/{folderType.ToSegment()}{year}/{fileId:N}{SanitizeExtension(extension)}";
        return ObjectKey.Create(key);
    }

    private static string SanitizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return string.Empty;
        var safe = new StringBuilder(".");
        foreach (var value in extension.AsSpan(1))
            if (char.IsAsciiLetterOrDigit(value))
                safe.Append(char.ToLowerInvariant(value));
        return safe.Length == 1 ? string.Empty : safe.ToString();
    }
}
