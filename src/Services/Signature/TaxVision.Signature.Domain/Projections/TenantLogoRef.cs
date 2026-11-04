using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Projections;

/// <summary>Superficie del logo de la oficina: lo que ve el equipo (CRM) o el cliente (Portal).</summary>
public enum TenantLogoSurface
{
    Crm,
    Portal,
}

public static class TenantLogoSurfaces
{
    /// <summary>Certificado y vista pública los ve el cliente: logo del portal, con el del CRM de respaldo.</summary>
    public static readonly IReadOnlyList<TenantLogoSurface> ClientPreference =
    [
        TenantLogoSurface.Portal,
        TenantLogoSurface.Crm,
    ];

    /// <summary>Lo que no es de una oficina (el logo del sistema en el certificado) sigue con el del CRM.</summary>
    public static readonly IReadOnlyList<TenantLogoSurface> StaffPreference = [TenantLogoSurface.Crm];

    /// <summary>Lee la superficie del contrato de Tenant. Un valor desconocido no se proyecta.</summary>
    public static bool TryParse(string? value, out TenantLogoSurface surface) =>
        Enum.TryParse(value, ignoreCase: true, out surface) && Enum.IsDefined(surface);
}

/// <summary>
/// Proyección local del logo de una oficina para UNA superficie, alimentada por
/// <c>TenantLogoUpdatedIntegrationEvent</c> / <c>TenantLogoRemovedIntegrationEvent</c> (Tenant es la
/// fuente de verdad). Clave (TenantId, Surface): retirar el logo de una superficie no toca la otra. El
/// retiro deja la fila sin archivo (conserva la fecha para ordenar). Un evento más viejo que lo ya
/// proyectado se ignora.
/// </summary>
public sealed class TenantLogoRef : ITenantOwned
{
    private TenantLogoRef() { }

    /// <summary>Satisface <see cref="ITenantOwned"/> para el HasQueryFilter global; TenantId se fija en Create.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public Guid TenantId { get; private set; }
    public TenantLogoSurface Surface { get; private set; }
    public Guid? LogoFileId { get; private set; }
    public string? LogoContentType { get; private set; }
    public long? LogoSizeBytes { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public bool HasLogo => LogoFileId is not null && LogoFileId != Guid.Empty;

    public static TenantLogoRef Create(Guid tenantId, TenantLogoSurface surface, DateTime updatedAtUtc) =>
        new()
        {
            TenantId = tenantId,
            Surface = surface,
            UpdatedAtUtc = updatedAtUtc,
        };

    /// <summary>Fija el logo. Devuelve false si el evento es más viejo que lo proyectado.</summary>
    public bool SetLogo(Guid fileId, string contentType, long sizeBytes, DateTime updatedAtUtc)
    {
        if (updatedAtUtc < UpdatedAtUtc)
            return false;

        LogoFileId = fileId;
        LogoContentType = contentType;
        LogoSizeBytes = sizeBytes;
        UpdatedAtUtc = updatedAtUtc;
        return true;
    }

    /// <summary>Quita el logo. Devuelve false si un logo más nuevo ya lo reemplazó.</summary>
    public bool RemoveLogo(DateTime removedAtUtc)
    {
        if (removedAtUtc < UpdatedAtUtc)
            return false;

        LogoFileId = null;
        LogoContentType = null;
        LogoSizeBytes = null;
        UpdatedAtUtc = removedAtUtc;
        return true;
    }
}
