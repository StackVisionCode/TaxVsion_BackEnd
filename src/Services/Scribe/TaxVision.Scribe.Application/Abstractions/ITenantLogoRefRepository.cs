using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Abstractions;

/// <summary>Logos proyectados del tenant, uno por superficie.</summary>
public interface ITenantLogoRefRepository
{
    Task<TenantLogoRef?> GetAsync(Guid tenantId, TenantLogoSurface surface, CancellationToken ct = default);

    /// <summary>Todas las superficies del tenant en una sola lectura (el resolver elige entre ellas).</summary>
    Task<IReadOnlyList<TenantLogoRef>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default);
}
