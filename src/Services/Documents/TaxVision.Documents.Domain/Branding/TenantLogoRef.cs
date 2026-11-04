namespace TaxVision.Documents.Domain.Branding;

/// <summary>
/// Proyección local del logo de la marca del tenant para UNA superficie (CRM o Portal), cuya fuente de
/// verdad es el servicio Tenant (TenantBrands). Se alimenta de <c>TenantLogoUpdatedIntegrationEvent</c> /
/// <c>TenantLogoRemovedIntegrationEvent</c> — mismo patrón que Signature y Scribe. Clave (TenantId,
/// Surface): quitar el logo de una superficie no toca la otra. Guarda SOLO el <c>fileId</c> de
/// CloudStorage; los bytes se bajan on-demand al renderizar el PDF. NO es <c>ITenantOwned</c> a
/// propósito: se escribe/lee desde consumers y desde la generación (scope Wolverine sin tenant
/// ambiental), por TenantId explícito, sin el filtro global fail-closed. Un evento más viejo que lo ya
/// proyectado se ignora.
/// </summary>
public sealed class TenantLogoRef
{
    private TenantLogoRef() { }

    public Guid TenantId { get; private set; }
    public TenantLogoSurface Surface { get; private set; }
    public Guid? LogoFileId { get; private set; }
    public string? LogoContentType { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public bool HasLogo => LogoFileId is not null && LogoFileId != Guid.Empty;

    public static TenantLogoRef Create(Guid tenantId, TenantLogoSurface surface, DateTime nowUtc) =>
        new()
        {
            TenantId = tenantId,
            Surface = surface,
            UpdatedAtUtc = nowUtc,
        };

    /// <summary>Fija el logo. Devuelve false si el evento es más viejo que lo proyectado.</summary>
    public bool SetLogo(Guid fileId, string contentType, DateTime nowUtc)
    {
        if (nowUtc < UpdatedAtUtc)
            return false;

        LogoFileId = fileId;
        LogoContentType = string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    /// <summary>Quita el logo. Devuelve false si un logo más nuevo ya lo reemplazó.</summary>
    public bool ClearLogo(DateTime nowUtc)
    {
        if (nowUtc < UpdatedAtUtc)
            return false;

        LogoFileId = null;
        LogoContentType = null;
        UpdatedAtUtc = nowUtc;
        return true;
    }
}
