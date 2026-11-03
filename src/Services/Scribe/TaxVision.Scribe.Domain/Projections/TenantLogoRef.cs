using BuildingBlocks.Domain;

namespace TaxVision.Scribe.Domain.Projections;

/// <summary>
/// Proyección local del logo de un tenant para UNA superficie (CRM o Portal), alimentada por
/// TenantLogoUpdatedIntegrationEvent / TenantLogoRemovedIntegrationEvent (Tenant es la fuente de
/// verdad). PK (TenantId, Surface): el retiro de una superficie no toca la otra. Soft-delete vía
/// DeletedAtUtc: LogoResolver la trata como "sin logo" si DeletedAtUtc no es null. Un evento más viejo
/// que lo ya proyectado se ignora (llegan fuera de orden).
/// </summary>
public sealed class TenantLogoRef : ITenantOwned
{
    private TenantLogoRef() { }

    /// <summary>
    /// RBAC Fase 5 (RBAC_Hardening_Plan.md) — implementación de <see cref="ITenantOwned"/> para el
    /// <c>HasQueryFilter</c> global de <c>ScribeDbContext</c>. <see cref="TenantId"/> ya se fija una
    /// sola vez en <see cref="Create"/> — este método existe solo para satisfacer la interfaz, EF
    /// Core nunca lo invoca.
    /// </summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public Guid TenantId { get; private set; }
    public TenantLogoSurface Surface { get; private set; }
    public Guid CloudStorageFileId { get; private set; }
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    public static TenantLogoRef Create(
        Guid tenantId,
        TenantLogoSurface surface,
        Guid cloudStorageFileId,
        string contentType,
        long sizeBytes,
        int? width,
        int? height,
        DateTime updatedAtUtc
    ) =>
        new()
        {
            TenantId = tenantId,
            Surface = surface,
            CloudStorageFileId = cloudStorageFileId,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Width = width,
            Height = height,
            UpdatedAtUtc = updatedAtUtc,
        };

    /// <summary>Aplica el logo confirmado. Devuelve false si el evento es más viejo que lo proyectado.</summary>
    public bool Update(
        Guid cloudStorageFileId,
        string contentType,
        long sizeBytes,
        int? width,
        int? height,
        DateTime updatedAtUtc
    )
    {
        if (updatedAtUtc < LastChangeUtc)
            return false;

        CloudStorageFileId = cloudStorageFileId;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        UpdatedAtUtc = updatedAtUtc;
        DeletedAtUtc = null;
        return true;
    }

    /// <summary>Marca el logo como retirado. Devuelve false si un logo más nuevo ya lo reemplazó.</summary>
    public bool MarkRemoved(DateTime removedAtUtc)
    {
        if (removedAtUtc < UpdatedAtUtc)
            return false;

        DeletedAtUtc = removedAtUtc;
        return true;
    }

    public bool IsActive => DeletedAtUtc is null;

    private DateTime LastChangeUtc =>
        DeletedAtUtc is { } deletedAt && deletedAt > UpdatedAtUtc ? deletedAt : UpdatedAtUtc;
}
