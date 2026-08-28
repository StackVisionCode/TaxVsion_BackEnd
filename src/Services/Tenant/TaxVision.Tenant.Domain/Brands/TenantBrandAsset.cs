using BuildingBlocks.Domain;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Domain.Brands;

/// <summary>
/// Un asset de marca (logo o favicon) dentro de una <see cref="TenantBrand"/>. Guarda el
/// <see cref="FileId"/> de CloudStorage — la IDENTIDAD del archivo, nunca una URL: la URL de hoy es
/// presignada y muere en minutos, y el email (CID) y el PDF (data-URI) necesitan los BYTES, no una
/// URL. La entidad es un contenedor de datos; las invariantes (tamaño, content-type) y las
/// transiciones de estado las decide el agregado. <see cref="Status"/> es explícito: un asset
/// <see cref="BrandAssetStatus.Pending"/> aún no pasó el antivirus y no debe servirse.
/// </summary>
public sealed class TenantBrandAsset : TenantEntity
{
    private TenantBrandAsset() { }

    public Guid TenantBrandId { get; private set; }
    public BrandAssetKey Key { get; private set; }
    public Guid FileId { get; private set; }
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public BrandAssetStatus Status { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    internal static TenantBrandAsset CreatePending(
        Guid tenantId,
        Guid brandId,
        BrandAssetKey key,
        Guid fileId,
        string contentType,
        long sizeBytes,
        int? width,
        int? height
    )
    {
        var now = DateTime.UtcNow;
        var entity = new TenantBrandAsset
        {
            Id = Guid.NewGuid(),
            TenantBrandId = brandId,
            Key = key,
            FileId = fileId,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Width = width,
            Height = height,
            Status = BrandAssetStatus.Pending,
            ConfirmedAtUtc = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        entity.SetTenant(tenantId);
        return entity;
    }

    /// <summary>Reemplaza el asset por un nuevo upload en curso (vuelve a Pending hasta el escaneo).</summary>
    internal void MarkPending(Guid fileId, string contentType, long sizeBytes, int? width, int? height)
    {
        FileId = fileId;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        Status = BrandAssetStatus.Pending;
        ConfirmedAtUtc = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Confirma con los metadatos REALES devueltos por CloudStorage tras el escaneo.</summary>
    internal void Confirm(string contentType, long sizeBytes, int? width, int? height, DateTime confirmedAtUtc)
    {
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        Status = BrandAssetStatus.Confirmed;
        ConfirmedAtUtc = confirmedAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
