using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Projections;

/// <summary>
/// Proyección local del tenant: nombre, subdominio y logo. La fuente de verdad es Tenant; esto se
/// alimenta de <c>TenantCreatedIntegrationEvent</c> y <c>TenantLogoUpdatedIntegrationEvent</c>, y el
/// backfill de arranque completa las oficinas anteriores a esos consumers.
/// </summary>
public sealed class TenantBrandingRef : ITenantOwned
{
    private TenantBrandingRef() { }

    /// <summary>Satisface <see cref="ITenantOwned"/> para el HasQueryFilter global; TenantId se fija en Create.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public Guid TenantId { get; private set; }
    public string OfficeName { get; private set; } = string.Empty;
    public string SubDomain { get; private set; } = string.Empty;
    public Guid? LogoFileId { get; private set; }
    public string? LogoContentType { get; private set; }
    public long? LogoSizeBytes { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantBrandingRef Create(Guid tenantId, DateTime updatedAtUtc) =>
        new() { TenantId = tenantId, UpdatedAtUtc = updatedAtUtc };

    public void SetOfficeName(string officeName, DateTime updatedAtUtc)
    {
        OfficeName = officeName?.Trim() ?? string.Empty;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void SetSubDomain(string subDomain, DateTime updatedAtUtc)
    {
        SubDomain = subDomain?.Trim().ToLowerInvariant() ?? string.Empty;
        UpdatedAtUtc = updatedAtUtc;
    }

    /// <summary>
    /// Completa nombre y subdominio SOLO donde están vacíos (backfill desde el directorio de Tenant). Lo que
    /// ya llegó por evento no se pisa: un evento en vuelo puede ser más nuevo que el listado. Devuelve si
    /// cambió algo, para no reescribir la fila en cada arranque.
    /// </summary>
    public bool FillMissingIdentity(string officeName, string subDomain, DateTime updatedAtUtc)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(OfficeName) && !string.IsNullOrWhiteSpace(officeName))
        {
            SetOfficeName(officeName, updatedAtUtc);
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(SubDomain) && !string.IsNullOrWhiteSpace(subDomain))
        {
            SetSubDomain(subDomain, updatedAtUtc);
            changed = true;
        }

        return changed;
    }

    public void SetLogo(Guid fileId, string contentType, long sizeBytes, DateTime updatedAtUtc)
    {
        LogoFileId = fileId;
        LogoContentType = contentType;
        LogoSizeBytes = sizeBytes;
        UpdatedAtUtc = updatedAtUtc;
    }

    /// <summary><c>true</c> si el tenant tiene un logo propio proyectado.</summary>
    public bool HasLogo => LogoFileId is not null && LogoFileId != Guid.Empty;
}
