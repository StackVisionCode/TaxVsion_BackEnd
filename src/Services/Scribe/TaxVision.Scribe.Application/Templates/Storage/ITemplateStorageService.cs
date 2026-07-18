using BuildingBlocks.Results;

namespace TaxVision.Scribe.Application.Templates.Storage;

/// <summary>Qué artefacto se está subiendo — gobierna el nombre de archivo y el content-type usados.</summary>
public enum TemplateArtifactKind
{
    Html,
    Text,
    DesignJson,
    PreviewImage,
}

/// <summary>Resultado de una subida: el FileId (lo único necesario para volver a leerlo) y el StorageKey (identificador legible/auditable guardado en el dominio).</summary>
public sealed record TemplateStorageUpload(Guid FileId, string StorageKey);

/// <summary>
/// Política de almacenamiento de Scribe sobre <see cref="Abstractions.ICloudStorageClient"/>: siempre
/// FolderType=Templates, siempre OwnerType=Tenant (con <see cref="BuildingBlocks.Tenancy.PlatformTenant.Id"/>
/// para contenido System). No conoce HTTP ni MinIO — eso es responsabilidad de la implementación de
/// ICloudStorageClient (Infrastructure).
/// </summary>
public interface ITemplateStorageService
{
    Task<Result<TemplateStorageUpload>> UploadAsync(
        Guid? tenantId,
        TemplateArtifactKind kind,
        byte[] content,
        Guid actorId,
        CancellationToken ct = default
    );

    Task<Result<string>> DownloadTextAsync(Guid fileId, Guid? tenantId, CancellationToken ct = default);
}
