using BuildingBlocks.Domain;

namespace TaxVision.Scribe.Domain.Projections;

/// <summary>
/// Proyección local del logo de un tenant, alimentada por TenantLogoUpdatedIntegrationEvent /
/// TenantLogoRemovedIntegrationEvent (Tenant es la fuente de verdad). PK simple TenantId — es
/// un 1:1, no tiene sentido un Id sintético. Soft-delete vía DeletedAtUtc: se conserva la fila
/// para no perder historial, LogoResolver la trata como "sin logo" si DeletedAtUtc no es null.
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
    public Guid CloudStorageFileId { get; private set; }
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    public static TenantLogoRef Create(
        Guid tenantId,
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
            CloudStorageFileId = cloudStorageFileId,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Width = width,
            Height = height,
            UpdatedAtUtc = updatedAtUtc,
        };

    public void Update(
        Guid cloudStorageFileId,
        string contentType,
        long sizeBytes,
        int? width,
        int? height,
        DateTime updatedAtUtc
    )
    {
        CloudStorageFileId = cloudStorageFileId;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        UpdatedAtUtc = updatedAtUtc;
        DeletedAtUtc = null;
    }

    public void MarkRemoved(DateTime removedAtUtc) => DeletedAtUtc = removedAtUtc;

    public bool IsActive => DeletedAtUtc is null;
}
