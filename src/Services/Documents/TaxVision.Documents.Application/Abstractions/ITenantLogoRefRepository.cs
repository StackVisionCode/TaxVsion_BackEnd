using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Application.Abstractions;

/// <summary>Proyección local del logo del tenant (fuente de verdad: servicio Tenant). Uno por superficie.</summary>
public interface ITenantLogoRefRepository
{
    Task<TenantLogoRef?> GetAsync(Guid tenantId, TenantLogoSurface surface, CancellationToken ct = default);

    /// <summary>Todas las superficies del tenant en una sola lectura.</summary>
    Task<IReadOnlyList<TenantLogoRef>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default);
}
