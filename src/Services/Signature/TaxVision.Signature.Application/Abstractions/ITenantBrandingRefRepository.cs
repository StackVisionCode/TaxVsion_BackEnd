using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>Acceso a la proyección local de marca del tenant (nombre + logo) para el certificado.</summary>
public interface ITenantBrandingRefRepository
{
    Task<TenantBrandingRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantBrandingRef branding, CancellationToken ct = default);
}
