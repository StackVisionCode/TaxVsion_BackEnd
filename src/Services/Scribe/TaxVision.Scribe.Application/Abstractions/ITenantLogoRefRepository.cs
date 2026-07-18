using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Abstractions;

public interface ITenantLogoRefRepository
{
    Task<TenantLogoRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default);
}
