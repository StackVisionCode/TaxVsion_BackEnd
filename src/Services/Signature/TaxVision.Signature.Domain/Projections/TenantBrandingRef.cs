using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Projections;

/// <summary>
/// Proyección local de la marca de un tenant para el certificado: nombre de la oficina (para
/// "Issued by") y, si la subió, el logo (fileId en CloudStorage). La fuente de verdad es Tenant;
/// esta fila se alimenta de <c>TenantCreatedIntegrationEvent</c> (nombre) y
/// <c>TenantLogoUpdatedIntegrationEvent</c> (logo). PK simple TenantId — es 1:1.
/// </summary>
public sealed class TenantBrandingRef : ITenantOwned
{
    private TenantBrandingRef() { }

    /// <summary>Satisface <see cref="ITenantOwned"/> para el HasQueryFilter global; TenantId se fija en Create.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public Guid TenantId { get; private set; }
    public string OfficeName { get; private set; } = string.Empty;
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
