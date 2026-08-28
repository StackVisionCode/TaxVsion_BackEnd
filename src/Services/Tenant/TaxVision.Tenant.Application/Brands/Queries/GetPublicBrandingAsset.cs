using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;

namespace TaxVision.Tenant.Application.Brands.Queries;

public sealed record GetPublicBrandingAssetQuery(Guid FileId);

/// <summary>
/// URL presignada del asset + su expiración real. El caller (controller) DEBE acotar el cache HTTP
/// del redirect a esta expiración: la URL vive poco (minutos) y un cache más largo dejaría al
/// navegador reusando una firma caducada (403 de MinIO) → imagen rota. Mismo patrón de descarga que
/// el resto del sistema (redirect a presigned de CloudStorage), no un proxy de bytes.
/// </summary>
public sealed record PublicBrandingAsset(Uri Url, DateTime ExpiresAtUtc);

/// <summary>
/// Cache-Control para el 302 del asset. El redirect apunta a una presigned de vida corta, así que el
/// cache del navegador NUNCA debe sobrevivir a la firma: se acota a la expiración real menos un margen
/// (jamás 'immutable'/1 año, aunque la ruta sea content-addressed — lo cacheable es el redirect, no los
/// bytes). Ya caducada → 'no-store' para forzar un redirect fresco en el próximo render.
/// </summary>
public static class BrandingAssetCachePolicy
{
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

    public static string CacheControl(DateTime expiresAtUtc, DateTime nowUtc)
    {
        var remaining = expiresAtUtc - nowUtc - Margin;
        var maxAge = remaining > TimeSpan.Zero ? (int)remaining.TotalSeconds : 0;
        return maxAge > 0 ? $"private, max-age={maxAge}" : "no-store";
    }
}

/// <summary>
/// Sirve un asset de marca sin sesión: valida que el fileId sea de un asset CONFIRMADO (nunca proxea
/// un fileId arbitrario ni uno pendiente de escaneo) y devuelve una URL presignada de CloudStorage.
/// El tenantId sale del propio asset, no del caller (que es anónimo).
/// </summary>
public static class GetPublicBrandingAssetHandler
{
    public static async Task<Result<PublicBrandingAsset>> Handle(
        GetPublicBrandingAssetQuery query,
        ITenantBrandRepository brandRepo,
        ITenantBrandingCloudStorageClient client,
        CancellationToken ct
    )
    {
        var asset = await brandRepo.GetConfirmedAssetByFileIdAsync(query.FileId, ct);
        if (asset is null)
            return Result.Failure<PublicBrandingAsset>(new Error("TenantBrand.Asset.NotFound", "Asset not found."));

        var urlResult = await client.GetDownloadUrlAsync(asset.TenantId, query.FileId, ct);
        if (urlResult.IsFailure)
            return Result.Failure<PublicBrandingAsset>(urlResult.Error);

        return Result.Success(new PublicBrandingAsset(urlResult.Value.Url, urlResult.Value.ExpiresAtUtc));
    }
}
