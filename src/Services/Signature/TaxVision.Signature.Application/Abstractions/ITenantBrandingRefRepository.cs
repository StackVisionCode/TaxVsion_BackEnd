using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Acceso a las proyecciones locales de marca de la oficina para el certificado y la vista pública:
/// identidad (<see cref="TenantBrandingRef"/>) y logos por superficie (<see cref="TenantLogoRef"/>).
/// </summary>
public interface ITenantBrandingRefRepository
{
    Task<TenantBrandingRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantBrandingRef branding, CancellationToken ct = default);

    /// <summary>Todas las filas, rastreadas. Solo para el backfill de arranque (una fila por oficina).</summary>
    Task<IReadOnlyList<TenantBrandingRef>> ListAllAsync(CancellationToken ct = default);

    Task<TenantLogoRef?> GetLogoAsync(Guid tenantId, TenantLogoSurface surface, CancellationToken ct = default);

    /// <summary>Los logos de todas las superficies de la oficina, en una sola lectura.</summary>
    Task<IReadOnlyList<TenantLogoRef>> ListLogosAsync(Guid tenantId, CancellationToken ct = default);

    Task AddLogoAsync(TenantLogoRef logo, CancellationToken ct = default);
}
