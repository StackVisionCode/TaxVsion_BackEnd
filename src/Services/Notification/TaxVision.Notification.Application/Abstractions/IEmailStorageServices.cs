using BuildingBlocks.Results;
using TaxVision.Notification.Domain.Emailing;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Referencias de almacenamiento devueltas al guardar los assets de una versión de plantilla/layout.</summary>
public sealed record StoredAssetRefs(
    string HtmlKey,
    Guid HtmlFileId,
    string? DesignKey,
    Guid? DesignFileId,
    string? PreviewKey,
    Guid? PreviewFileId
);

/// <summary>
/// Almacena y recupera los assets de plantillas en CloudStorage, construyendo las storage keys lógicas
/// (system/templates/... o tenants/{tenantId}/templates/...). No duplica lógica de almacenamiento:
/// delega en <see cref="ICloudStorageClient"/>.
/// </summary>
public interface ITemplateStorageService
{
    Task<Result<StoredAssetRefs>> StoreVersionAsync(
        EmailScope scope,
        Guid? tenantId,
        string templateKey,
        int version,
        string html,
        string? designJson,
        byte[]? previewPng,
        CancellationToken ct = default
    );

    Task<Result<string>> GetHtmlAsync(Guid htmlFileId, CancellationToken ct = default);
}

/// <summary>Almacena y recupera los assets de layouts en CloudStorage.</summary>
public interface ILayoutStorageService
{
    Task<Result<StoredAssetRefs>> StoreAsync(
        EmailScope scope,
        Guid? tenantId,
        string layoutName,
        string html,
        string? designJson,
        byte[]? previewPng,
        CancellationToken ct = default
    );

    Task<Result<string>> GetHtmlAsync(Guid htmlFileId, CancellationToken ct = default);
}
