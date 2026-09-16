using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Application.Abstractions;

/// <summary>Proyección local del logo del tenant (fuente de verdad: servicio Tenant). 1:1 por tenant.</summary>
public interface ITenantLogoRefRepository
{
    Task<TenantLogoRef?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default);
}
