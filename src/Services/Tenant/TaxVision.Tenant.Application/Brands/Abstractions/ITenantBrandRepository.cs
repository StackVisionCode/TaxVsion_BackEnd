using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands.Abstractions;

/// <summary>
/// Acceso a las marcas de un tenant. La entidad es <c>ITenantOwned</c>, así que el impl DEBE usar
/// <c>IgnoreQueryFilters()</c> + un tenantId explícito (guardrail #8) — el filtro global fail-closed
/// devolvería cero filas en un scope sin tenant ambiental. Siempre trae Colors y Assets con Include.
/// </summary>
public interface ITenantBrandRepository
{
    Task<TenantBrand?> GetAsync(Guid tenantId, BrandSurface surface, CancellationToken ct = default);

    Task<IReadOnlyList<TenantBrand>> ListAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(TenantBrand brand, CancellationToken ct = default);

    /// <summary>Busca un asset CONFIRMADO por su fileId, cruzando tenants (para el servido público
    /// anónimo). Solo confirmados: un asset Pending nunca se sirve. Devuelve null si no existe o no
    /// está confirmado — el endpoint público responde 404 en ambos casos, sin distinguirlos.</summary>
    Task<TenantBrandAsset?> GetConfirmedAssetByFileIdAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>Trae la marca (con Assets) que contiene un asset con ese fileId dentro del tenant —
    /// para correlacionar el resultado del escaneo (que solo trae TenantId+FileId) contra la marca y
    /// su assetKey. Cualquier estado (el asset a confirmar está Pending). Null si no es nuestro.</summary>
    Task<TenantBrand?> GetByAssetFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
}
