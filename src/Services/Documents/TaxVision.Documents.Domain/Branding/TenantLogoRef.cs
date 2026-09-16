namespace TaxVision.Documents.Domain.Branding;

/// <summary>
/// Proyección local (1:1 por tenant) del logo de la marca del tenant, cuya fuente de verdad es el
/// servicio Tenant (TenantBrands, superficie CRM). Se alimenta de
/// <c>TenantLogoUpdatedIntegrationEvent</c> / <c>TenantLogoRemovedIntegrationEvent</c> — mismo patrón
/// que Signature (TenantBrandingRef) y Scribe (TenantLogoRef). Guarda SOLO el <c>fileId</c> de
/// CloudStorage; los bytes se bajan on-demand al renderizar el PDF. NO es <c>ITenantOwned</c> a
/// propósito: se escribe/lee desde consumers y desde la generación (scope Wolverine sin tenant
/// ambiental), por TenantId explícito, sin el filtro global fail-closed.
/// </summary>
public sealed class TenantLogoRef
{
    private TenantLogoRef() { }

    public Guid TenantId { get; private set; }
    public Guid? LogoFileId { get; private set; }
    public string? LogoContentType { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public bool HasLogo => LogoFileId is not null && LogoFileId != Guid.Empty;

    public static TenantLogoRef Create(Guid tenantId, DateTime nowUtc) =>
        new() { TenantId = tenantId, UpdatedAtUtc = nowUtc };

    public void SetLogo(Guid fileId, string contentType, DateTime nowUtc)
    {
        LogoFileId = fileId;
        LogoContentType = string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
        UpdatedAtUtc = nowUtc;
    }

    public void ClearLogo(DateTime nowUtc)
    {
        LogoFileId = null;
        LogoContentType = null;
        UpdatedAtUtc = nowUtc;
    }
}
