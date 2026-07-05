using System.Text;
using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing;

namespace TaxVision.Notification.Infrastructure.Storage;

/// <summary>
/// Guarda los assets de una versión de plantilla en CloudStorage bajo claves lógicas
/// (system/templates/... o tenants/{tenantId}/templates/...). El handle durable es el FileId de
/// CloudStorage; la storage key es una etiqueta lógica para auditoría/lectura.
/// </summary>
public sealed partial class TemplateStorageService(ICloudStorageClient client) : ITemplateStorageService
{
    public async Task<Result<StoredAssetRefs>> StoreVersionAsync(
        EmailScope scope,
        Guid? tenantId,
        string templateKey,
        int version,
        string html,
        string? designJson,
        byte[]? previewPng,
        CancellationToken ct = default
    )
    {
        var prefix =
            scope == EmailScope.System
                ? $"system/templates/{Slug(templateKey)}/v{version}"
                : $"tenants/{tenantId:N}/templates/{Slug(templateKey)}/v{version}";

        var htmlUpload = await client.UploadAsync(
            new CloudStorageUpload(Encoding.UTF8.GetBytes(html), "template.html", "text/html", "Tenant", null, "Other", null),
            tenantId,
            ct
        );
        if (htmlUpload.IsFailure)
            return Result.Failure<StoredAssetRefs>(htmlUpload.Error);

        string? designKey = null;
        Guid? designFileId = null;
        if (!string.IsNullOrWhiteSpace(designJson))
        {
            var up = await client.UploadAsync(
                new CloudStorageUpload(Encoding.UTF8.GetBytes(designJson), "design.json", "application/json", "Tenant", null, "Other", null),
                tenantId,
                ct
            );
            if (up.IsFailure)
                return Result.Failure<StoredAssetRefs>(up.Error);
            designKey = $"{prefix}/design.json";
            designFileId = up.Value;
        }

        string? previewKey = null;
        Guid? previewFileId = null;
        if (previewPng is { Length: > 0 })
        {
            var up = await client.UploadAsync(
                new CloudStorageUpload(previewPng, "preview.png", "image/png", "Tenant", null, "Other", null),
                tenantId,
                ct
            );
            if (up.IsFailure)
                return Result.Failure<StoredAssetRefs>(up.Error);
            previewKey = $"{prefix}/preview.png";
            previewFileId = up.Value;
        }

        return Result.Success(
            new StoredAssetRefs($"{prefix}/template.html", htmlUpload.Value, designKey, designFileId, previewKey, previewFileId)
        );
    }

    public Task<Result<string>> GetHtmlAsync(Guid htmlFileId, CancellationToken ct = default) =>
        client.DownloadTextAsync(htmlFileId, null, ct);

    internal static string Slug(string value) => SlugRegex().Replace(value.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}

/// <summary>Guarda los assets de un layout en CloudStorage bajo system/layouts/... o tenants/{tenantId}/layouts/...</summary>
public sealed class LayoutStorageService(ICloudStorageClient client) : ILayoutStorageService
{
    public async Task<Result<StoredAssetRefs>> StoreAsync(
        EmailScope scope,
        Guid? tenantId,
        string layoutName,
        string html,
        string? designJson,
        byte[]? previewPng,
        CancellationToken ct = default
    )
    {
        var prefix =
            scope == EmailScope.System
                ? $"system/layouts/{TemplateStorageService.Slug(layoutName)}"
                : $"tenants/{tenantId:N}/layouts/{TemplateStorageService.Slug(layoutName)}";

        var htmlUpload = await client.UploadAsync(
            new CloudStorageUpload(Encoding.UTF8.GetBytes(html), "layout.html", "text/html", "Tenant", null, "Other", null),
            tenantId,
            ct
        );
        if (htmlUpload.IsFailure)
            return Result.Failure<StoredAssetRefs>(htmlUpload.Error);

        string? designKey = null;
        Guid? designFileId = null;
        if (!string.IsNullOrWhiteSpace(designJson))
        {
            var up = await client.UploadAsync(
                new CloudStorageUpload(Encoding.UTF8.GetBytes(designJson), "design.json", "application/json", "Tenant", null, "Other", null),
                tenantId,
                ct
            );
            if (up.IsFailure)
                return Result.Failure<StoredAssetRefs>(up.Error);
            designKey = $"{prefix}/design.json";
            designFileId = up.Value;
        }

        string? previewKey = null;
        Guid? previewFileId = null;
        if (previewPng is { Length: > 0 })
        {
            var up = await client.UploadAsync(
                new CloudStorageUpload(previewPng, "preview.png", "image/png", "Tenant", null, "Other", null),
                tenantId,
                ct
            );
            if (up.IsFailure)
                return Result.Failure<StoredAssetRefs>(up.Error);
            previewKey = $"{prefix}/preview.png";
            previewFileId = up.Value;
        }

        return Result.Success(
            new StoredAssetRefs($"{prefix}/layout.html", htmlUpload.Value, designKey, designFileId, previewKey, previewFileId)
        );
    }

    public Task<Result<string>> GetHtmlAsync(Guid htmlFileId, CancellationToken ct = default) =>
        client.DownloadTextAsync(htmlFileId, null, ct);
}
