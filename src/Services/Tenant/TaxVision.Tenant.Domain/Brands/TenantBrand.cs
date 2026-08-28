using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Domain.Brands;

/// <summary>
/// Identidad visual de un tenant para UNA superficie (CRM, portal, etc.). Raíz del agregado: dueña
/// de sus colores y assets, que solo se tocan a través de sus métodos. Reemplaza las 10 columnas de
/// branding sueltas de la tabla <c>Tenants</c> — el modelo crece por FILAS (un color / asset nuevo
/// es una fila), no por columnas (una migración de esquema).
///
/// <para>La cascada de defaults (token del tenant → marca del sistema → constante compilada) NO vive
/// aquí: el agregado guarda solo lo que el tenant personalizó. La resolución al default la hace la
/// capa Application al leer, combinando esta marca con la del tenant de plataforma.</para>
/// </summary>
public sealed class TenantBrand : TenantEntity
{
    /// <summary>500KB — mismo valor que el modelo viejo (<c>Tenant.MaxLogoSizeBytes</c>) y que
    /// <c>CloudStorageOptions.BrandingPolicy()</c>. Son tres validaciones independientes del mismo
    /// invariante de negocio; hay que mantenerlas en sync (consolidarlas es trabajo de otra fase).</summary>
    public const long MaxAssetSizeBytes = 500L * 1024;

    private static readonly HashSet<string> AllowedAssetContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/svg+xml",
    };

    private readonly List<TenantBrandColor> _colors = [];
    private readonly List<TenantBrandAsset> _assets = [];

    private TenantBrand() { }

    public BrandSurface Surface { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<TenantBrandColor> Colors => _colors.AsReadOnly();
    public IReadOnlyCollection<TenantBrandAsset> Assets => _assets.AsReadOnly();

    public static TenantBrand Create(Guid tenantId, BrandSurface surface)
    {
        var now = DateTime.UtcNow;
        var brand = new TenantBrand
        {
            Id = Guid.NewGuid(),
            Surface = surface,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        brand.SetTenant(tenantId);
        return brand;
    }

    // ----- Colores -----

    /// <summary>Fija (crea o actualiza) el color de un token. Valida el hex antes de tocar nada.</summary>
    public Result SetColor(BrandColorToken token, string hex)
    {
        var color = HexColor.Create(hex);
        if (color.IsFailure)
            return Result.Failure(color.Error);

        var existing = FindColor(token);
        if (existing is null)
            _colors.Add(TenantBrandColor.Create(TenantId, Id, token, color.Value));
        else
            existing.Update(color.Value);

        Touch();
        return Result.Success();
    }

    /// <summary>Quita un color para que vuelva al default. Idempotente: no falla si no estaba.</summary>
    public void RemoveColor(BrandColorToken token)
    {
        var existing = FindColor(token);
        if (existing is null)
            return;

        _colors.Remove(existing);
        Touch();
    }

    /// <summary>Vuelve la superficie entera al default del sistema (quita todos los colores).</summary>
    public void ResetColors()
    {
        if (_colors.Count == 0)
            return;

        _colors.Clear();
        Touch();
    }

    // ----- Assets (logo / favicon) -----

    /// <summary>Registra un upload en curso con los metadatos DECLARADOS por el cliente. Queda en
    /// <see cref="BrandAssetStatus.Pending"/> hasta que CloudStorage confirme el escaneo antivirus.</summary>
    public Result SetAssetPending(
        BrandAssetKey key,
        Guid fileId,
        string contentType,
        long sizeBytes,
        int? width,
        int? height
    )
    {
        var validation = ValidateAsset(fileId, contentType, sizeBytes);
        if (validation.IsFailure)
            return validation;

        var existing = FindAsset(key);
        if (existing is null)
            _assets.Add(
                TenantBrandAsset.CreatePending(TenantId, Id, key, fileId, contentType, sizeBytes, width, height)
            );
        else
            existing.MarkPending(fileId, contentType, sizeBytes, width, height);

        Touch();
        return Result.Success();
    }

    /// <summary>Confirma un asset ya escaneado. Solo actúa si el <paramref name="fileId"/> coincide
    /// con el pendiente: si el tenant ya reemplazó el asset, este resultado llega tarde y se ignora
    /// (idempotente ante replays del evento de escaneo).</summary>
    public Result ConfirmAsset(
        BrandAssetKey key,
        Guid fileId,
        string contentType,
        long sizeBytes,
        int? width,
        int? height,
        DateTime confirmedAtUtc
    )
    {
        var validation = ValidateAsset(fileId, contentType, sizeBytes);
        if (validation.IsFailure)
            return validation;

        var existing = FindAsset(key);
        if (existing is null || existing.FileId != fileId)
            return Result.Success();

        existing.Confirm(contentType, sizeBytes, width, height, confirmedAtUtc);
        Touch();
        return Result.Success();
    }

    /// <summary>Descarta un upload que CloudStorage rechazó (infectado / bloqueado), solo si el
    /// fileId pendiente coincide (si ya se reemplazó, no pisa el asset nuevo). Idempotente.</summary>
    public void DiscardPendingAsset(BrandAssetKey key, Guid fileId)
    {
        var existing = FindAsset(key);
        if (existing is null || existing.FileId != fileId || existing.Status != BrandAssetStatus.Pending)
            return;

        _assets.Remove(existing);
        Touch();
    }

    /// <summary>Elimina un asset. Idempotente: no falla si no estaba.</summary>
    public void RemoveAsset(BrandAssetKey key)
    {
        var existing = FindAsset(key);
        if (existing is null)
            return;

        _assets.Remove(existing);
        Touch();
    }

    // ----- Internos (sin LINQ: guardrail #4) -----

    private TenantBrandColor? FindColor(BrandColorToken token)
    {
        foreach (var color in _colors)
        {
            if (color.Token == token)
                return color;
        }

        return null;
    }

    private TenantBrandAsset? FindAsset(BrandAssetKey key)
    {
        foreach (var asset in _assets)
        {
            if (asset.Key == key)
                return asset;
        }

        return null;
    }

    private static Result ValidateAsset(Guid fileId, string contentType, long sizeBytes)
    {
        if (fileId == Guid.Empty)
            return Result.Failure(new Error("TenantBrand.Asset.FileId", "FileId is required."));

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedAssetContentTypes.Contains(contentType))
        {
            return Result.Failure(
                new Error(
                    "TenantBrand.Asset.ContentType",
                    "Asset content type must be image/png, image/jpeg, or image/svg+xml."
                )
            );
        }

        if (sizeBytes <= 0 || sizeBytes > MaxAssetSizeBytes)
        {
            return Result.Failure(
                new Error("TenantBrand.Asset.SizeBytes", $"Asset size must be between 1 and {MaxAssetSizeBytes} bytes.")
            );
        }

        return Result.Success();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
