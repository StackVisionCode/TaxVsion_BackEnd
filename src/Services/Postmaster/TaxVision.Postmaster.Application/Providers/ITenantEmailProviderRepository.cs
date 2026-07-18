using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Application.Providers;

public interface ITenantEmailProviderRepository
{
    Task AddAsync(TenantEmailProvider provider, CancellationToken ct = default);

    Task<Result<TenantEmailProvider>> GetByCodeAsync(
        Guid tenantId,
        string providerCode,
        CancellationToken ct = default
    );

    /// <summary>El único provider habilitado del tenant. Nunca cae a otro tenant ni a System.</summary>
    Task<Result<TenantEmailProvider>> GetEnabledByTenantAsync(Guid tenantId, CancellationToken ct = default);
}
