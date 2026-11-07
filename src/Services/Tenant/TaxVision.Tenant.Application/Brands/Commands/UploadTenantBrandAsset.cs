using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Tenants;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;
using Wolverine.Attributes;

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

/// <summary>El archivo ya está en MinIO: falta guardarlo como Pending y pedir el escaneo.</summary>
public sealed record SaveTenantBrandAssetCommand(
    Guid TenantId,
    BrandSurface Surface,
    BrandAssetKey Key,
    TenantLogoUpload Upload,
    TenantBrandStoredFile Stored
);

/// <param name="EmailWarning">
/// Aviso para mostrarle al usuario, o null si no hay nada que advertir. No es un error: el asset se
/// sube igual. Hoy solo cubre el SVG, que el navegador renderiza y el correo no.
/// </param>
public sealed record UploadTenantBrandAssetResponse(Guid FileId, string Status, string? EmailWarning = null);

/// <summary>
/// El logo del CRM es el que viaja a los correos (Scribe). Los clientes de correo no renderizan SVG,
/// asi que uno subido ahi se acepta pero no se usa: el correo cae al nombre de la oficina en texto y
/// hasta ahora nadie se lo decia al dueno, que lo veia perfecto en el CRM.
/// </summary>
public static class BrandAssetEmailWarnings
{
    private const string Svg = "image/svg+xml";

    public static string? For(BrandSurface surface, BrandAssetKey key, string contentType) =>
        surface == BrandSurface.Crm
        && key == BrandAssetKey.Logo
        && string.Equals(contentType, Svg, StringComparison.OrdinalIgnoreCase)
            ? "Email clients cannot display SVG, so your emails will show your office name as text instead of this logo. Upload a PNG or JPEG to use it in email too."
            : null;
}

/// <summary>
/// Sube logo o favicon (mismo pipeline asíncrono que el logo viejo: MinIO + SaveFileRequested para el
/// escaneo). Setea el asset en Pending de forma optimista con los metadatos declarados; se confirma
/// cuando llega el resultado del escaneo (Fase 6 — consumer). Reusa el cliente de CloudStorage y el
/// lector de dimensiones existentes. El PUT va sin transacción abierta; un choque al guardar reintenta solo
/// <see cref="SaveTenantBrandAssetHandler"/>, sin volver a subir el archivo.
/// </summary>
public static class UploadTenantBrandAssetHandler
{
    [NonTransactional]
    public static async Task<Result<UploadTenantBrandAssetResponse>> Handle(
        UploadTenantBrandAssetCommand cmd,
        ITenantBrandingCloudStorageClient client,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var upload = new TenantLogoUpload(cmd.Content, cmd.ContentType, cmd.FileName, cmd.ActorId);

        // 1) Solo subir a MinIO (aún NO pedir el escaneo).
        var stored = await client.StoreAsync(cmd.TenantId, upload, ct);
        if (stored.IsFailure)
            return Result.Failure<UploadTenantBrandAssetResponse>(stored.Error);

        return await bus.InvokeAsync<Result<UploadTenantBrandAssetResponse>>(
            new SaveTenantBrandAssetCommand(cmd.TenantId, cmd.Surface, cmd.Key, upload, stored.Value),
            ct
        );
    }
}

public static class SaveTenantBrandAssetHandler
{
    public static async Task<Result<UploadTenantBrandAssetResponse>> Handle(
        SaveTenantBrandAssetCommand cmd,
        ITenantBrandRepository repo,
        ITenantBrandingCloudStorageClient client,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        CancellationToken ct
    )
    {
        var upload = cmd.Upload;
        var fileId = cmd.Stored.FileId;
        var (width, height) = LogoImageDimensionReader.TryRead(upload.Content, upload.ContentType);

        // 2) Persistir el asset Pending ANTES de disparar el escaneo. Si publicáramos primero, el
        //    resultado del escaneo podría llegar antes de que exista la fila y quedaría huérfana en
        //    Pending para siempre (el evento de confirmación no se repite).
        var brand = await BrandCommandSupport.GetOrCreateAsync(repo, cmd.TenantId, cmd.Surface, ct);
        var setResult = brand.SetAssetPending(
            cmd.Key,
            fileId,
            upload.ContentType,
            upload.Content.LongLength,
            width,
            height
        );
        if (setResult.IsFailure)
            return Result.Failure<UploadTenantBrandAssetResponse>(setResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        // 3) Ahora sí: pedir a CloudStorage que catalogue y escanee. El consumer del resultado ya
        //    encontrará la fila Pending y podrá confirmarla.
        await client.RequestCatalogAsync(cmd.TenantId, upload, cmd.Stored, ct);

        await BrandCommandSupport.InvalidateAsync(cache, cmd.TenantId, cmd.Surface, ct);
        return Result.Success(
            new UploadTenantBrandAssetResponse(
                fileId,
                "processing",
                BrandAssetEmailWarnings.For(cmd.Surface, cmd.Key, upload.ContentType)
            )
        );
    }
}
