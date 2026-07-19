using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain;

/// <summary>
/// Soporte de logo por tenant (Tenant_Service_LogoSupport_Plan.md) — embebido por Postmaster como
/// inline attachment CID en cada correo saliente al tenant (ver Scribe LogoResolver/TenantLogoRef,
/// Scribe Fase 4.5). <see cref="Tenant.LogoFileId"/> se setea de forma OPTIMISTA al iniciar el
/// upload (<see cref="SetLogoPending"/>, antes de que CloudStorage confirme el escaneo antivirus)
/// para poder correlacionar la respuesta asincrona — ver TenantBrandingFileScanResultConsumer
/// (Application) — y solo se considera "confirmado" cuando <see cref="ConfirmLogo"/> corre y deja
/// <see cref="LogoUpdatedAtUtc"/> no nulo.
/// </summary>
public partial class Tenant
{
    /// <summary>200KB del borrador original del plan, subido a 500KB: dejaba muy poco margen para un
    /// PNG con transparencia en retina (2x) sin forzar a los tenants a comprimir agresivamente su
    /// marca. Sigue muy por debajo del cap de 5MB que Postmaster aplica a la suma de inline assets de
    /// un mismo email (SentMessage.MaxTotalInlineAssetsBytes) y de la policy de CloudStorage para
    /// FolderType.Branding (mismo valor, ver CloudStorageOptions.BrandingPolicy — deben mantenerse en
    /// sync porque son dos validaciones independientes del mismo invariante de negocio).</summary>
    public const long MaxLogoSizeBytes = 500L * 1024;

    private static readonly HashSet<string> AllowedLogoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/svg+xml",
    };

    public Guid? LogoFileId { get; private set; }
    public string? LogoContentType { get; private set; }
    public long? LogoSizeBytes { get; private set; }
    public int? LogoWidth { get; private set; }
    public int? LogoHeight { get; private set; }
    public DateTime? LogoUpdatedAtUtc { get; private set; }

    /// <summary>
    /// Registra un upload en curso con los metadatos DECLARADOS por el cliente (mismo criterio que
    /// InitiateUploadRequest.SizeBytes en CloudStorage — el tamaño real recién se conoce tras el
    /// escaneo). Deja <see cref="LogoUpdatedAtUtc"/> en null a propósito: ese campo es el único
    /// marcador de "confirmado" que usan <see cref="DiscardPendingLogo"/> y GetTenantLogoQuery, asi
    /// que esta llamada NUNCA debe setearlo o ambos dejan de poder distinguir un logo pendiente de
    /// escaneo de uno ya disponible.
    /// </summary>
    public Result SetLogoPending(Guid fileId, string contentType, long sizeBytes, int? width, int? height)
    {
        var validation = ValidateLogo(fileId, contentType, sizeBytes);
        if (validation.IsFailure)
            return validation;

        LogoFileId = fileId;
        LogoContentType = contentType;
        LogoSizeBytes = sizeBytes;
        LogoWidth = width;
        LogoHeight = height;
        LogoUpdatedAtUtc = null;
        return Result.Success();
    }

    /// <summary>
    /// Confirma un logo ya disponible en CloudStorage — llamado desde
    /// TenantBrandingFileScanResultConsumer al recibir FileAvailableIntegrationEvent, con los
    /// metadatos REALES devueltos por CloudStorage (pueden diferir levemente de los declarados, ej.
    /// compresión del lado del storage). Es la única llamada que setea LogoUpdatedAtUtc.
    /// </summary>
    public Result ConfirmLogo(
        Guid fileId,
        string contentType,
        long sizeBytes,
        int? width,
        int? height,
        DateTime confirmedAtUtc
    )
    {
        var validation = ValidateLogo(fileId, contentType, sizeBytes);
        if (validation.IsFailure)
            return validation;

        LogoFileId = fileId;
        LogoContentType = contentType;
        LogoSizeBytes = sizeBytes;
        LogoWidth = width;
        LogoHeight = height;
        LogoUpdatedAtUtc = confirmedAtUtc;
        return Result.Success();
    }

    private static Result ValidateLogo(Guid fileId, string contentType, long sizeBytes)
    {
        if (fileId == Guid.Empty)
            return Result.Failure(new Error("Tenant.Logo.FileId", "FileId is required."));

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedLogoContentTypes.Contains(contentType))
        {
            return Result.Failure(
                new Error(
                    "Tenant.Logo.ContentType",
                    "Logo content type must be image/png, image/jpeg, or image/svg+xml."
                )
            );
        }

        if (sizeBytes <= 0 || sizeBytes > MaxLogoSizeBytes)
        {
            return Result.Failure(
                new Error("Tenant.Logo.SizeBytes", $"Logo size must be between 1 and {MaxLogoSizeBytes} bytes.")
            );
        }

        return Result.Success();
    }

    /// <summary>Idempotente: no falla si ya no había logo — mismo criterio que un DELETE HTTP idempotente.</summary>
    public void RemoveLogo()
    {
        LogoFileId = null;
        LogoContentType = null;
        LogoSizeBytes = null;
        LogoWidth = null;
        LogoHeight = null;
        LogoUpdatedAtUtc = null;
    }

    /// <summary>
    /// Descarta un upload en curso que CloudStorage rechazó (infectado / bloqueado por política) —
    /// solo si el fileId pendiente coincide (si el tenant ya reemplazó el logo mientras tanto, este
    /// rechazo llega tarde y no debe pisar el logo nuevo que sí se confirmó).
    /// </summary>
    public void DiscardPendingLogo(Guid fileId)
    {
        if (LogoFileId == fileId && LogoUpdatedAtUtc is null)
            RemoveLogo();
    }
}
