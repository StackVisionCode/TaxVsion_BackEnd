using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Projections;

/// <summary>
/// Proyección local de la identidad de la oficina: nombre y subdominio (los logos viven en
/// <see cref="TenantLogoRef"/>, uno por superficie). La fuente de verdad es Tenant; esto se alimenta de
/// <c>TenantCreatedIntegrationEvent</c>, <c>TenantUpdatedIntegrationEvent</c> (renombre) y
/// <c>TenantSubdomainChangedIntegrationEvent</c>, y el backfill de arranque completa las oficinas
/// anteriores a esos consumers. <see cref="OfficeNameAsOfUtc"/> ordena los cambios de nombre: uno más
/// viejo que el aplicado se ignora. El nombre comercial llega por <c>TenantProfileChangedIntegrationEvent</c>
/// y se ordena aparte con <see cref="ProfileVersion"/>.
/// </summary>
public sealed class TenantBrandingRef : ITenantOwned
{
    private TenantBrandingRef() { }

    /// <summary>Satisface <see cref="ITenantOwned"/> para el HasQueryFilter global; TenantId se fija en Create.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public Guid TenantId { get; private set; }
    public string OfficeName { get; private set; } = string.Empty;
    public string SubDomain { get; private set; } = string.Empty;

    /// <summary>Fecha de Tenant del nombre aplicado por evento; null = nunca llegó uno con fecha.</summary>
    public DateTime? OfficeNameAsOfUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Nombre comercial (DBA) del perfil de la oficina; null = sin dato.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Versión del perfil aplicado; 0 = nunca llegó uno.</summary>
    public long ProfileVersion { get; private set; }

    /// <summary>Nombre que ve el firmante (certificado): el comercial si existe, si no el de la oficina.</summary>
    public string? IssuerDisplayName => DisplayName ?? (string.IsNullOrWhiteSpace(OfficeName) ? null : OfficeName);

    public static TenantBrandingRef Create(Guid tenantId, DateTime updatedAtUtc) =>
        new() { TenantId = tenantId, UpdatedAtUtc = updatedAtUtc };

    public void SetOfficeName(string officeName, DateTime updatedAtUtc)
    {
        OfficeName = officeName?.Trim() ?? string.Empty;
        UpdatedAtUtc = updatedAtUtc;
    }

    /// <summary>
    /// Aplica el nombre que trae un evento de Tenant con su fecha. Devuelve false (sin cambios) si ya hay
    /// un nombre aplicado más nuevo: los eventos pueden llegar fuera de orden.
    /// </summary>
    public bool ApplyOfficeName(string officeName, DateTime asOfUtc, DateTime updatedAtUtc)
    {
        if (OfficeNameAsOfUtc is { } current && asOfUtc < current)
            return false;

        SetOfficeName(officeName, updatedAtUtc);
        OfficeNameAsOfUtc = asOfUtc;
        return true;
    }

    /// <summary>
    /// Aplica el perfil de la oficina. Devuelve false (sin cambios) si la versión no es más nueva que la
    /// aplicada: el evento puede repetirse o llegar fuera de orden.
    /// </summary>
    public bool ApplyProfile(long version, string? displayName, DateTime updatedAtUtc)
    {
        if (version <= ProfileVersion)
            return false;

        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        ProfileVersion = version;
        UpdatedAtUtc = updatedAtUtc;
        return true;
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
}
