using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Tenants;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands.Commands;

public sealed record UploadTenantBrandAssetCommand(
    Guid TenantId,
    BrandSurface Surface,
    BrandAssetKey Key,
    Guid ActorId,
    byte[] Content,
    string ContentType,
    string FileName
);

public sealed record UploadTenantBrandAssetResponse(Guid FileId, string Status);

/// <summary>
/// Sube logo o favicon (mismo pipeline asíncrono que el logo viejo: MinIO + SaveFileRequested para el
/// escaneo). Setea el asset en Pending de forma optimista con los metadatos declarados; se confirma
/// cuando llega el resultado del escaneo (Fase 6 — consumer). Reusa el cliente de CloudStorage y el
/// lector de dimensiones existentes.
/// </summary>
public static class UploadTenantBrandAssetHandler
{
    public static async Task<Result<UploadTenantBrandAssetResponse>> Handle(
        UploadTenantBrandAssetCommand cmd,
        ITenantBrandRepository repo,
        ITenantBrandingCloudStorageClient client,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        CancellationToken ct
    )
    {
        var upload = new TenantLogoUpload(cmd.Content, cmd.ContentType, cmd.FileName, cmd.ActorId);

        // 1) Solo subir a MinIO (aún NO pedir el escaneo).
        var stored = await client.StoreAsync(cmd.TenantId, upload, ct);
        if (stored.IsFailure)
            return Result.Failure<UploadTenantBrandAssetResponse>(stored.Error);

        var fileId = stored.Value.FileId;
        var (width, height) = LogoImageDimensionReader.TryRead(cmd.Content, cmd.ContentType);

        // 2) Persistir el asset Pending ANTES de disparar el escaneo. Si publicáramos primero, el
        //    resultado del escaneo podría llegar antes de que exista la fila y quedaría huérfana en
        //    Pending para siempre (el evento de confirmación no se repite).
        var brand = await BrandCommandSupport.GetOrCreateAsync(repo, cmd.TenantId, cmd.Surface, ct);
        var setResult = brand.SetAssetPending(cmd.Key, fileId, cmd.ContentType, cmd.Content.LongLength, width, height);
        if (setResult.IsFailure)
            return Result.Failure<UploadTenantBrandAssetResponse>(setResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        // 3) Ahora sí: pedir a CloudStorage que catalogue y escanee. El consumer del resultado ya
        //    encontrará la fila Pending y podrá confirmarla.
        await client.RequestCatalogAsync(cmd.TenantId, upload, stored.Value, ct);

        await BrandCommandSupport.InvalidateAsync(cache, cmd.TenantId, cmd.Surface, ct);
        return Result.Success(new UploadTenantBrandAssetResponse(fileId, "processing"));
    }
}
