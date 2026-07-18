using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Application.Providers;

public interface IProviderHealthStatusRepository
{
    Task AddAsync(ProviderHealthStatus status, CancellationToken ct = default);

    Task<Result<ProviderHealthStatus>> GetAsync(
        ProviderKind providerKind,
        Guid? tenantId,
        string providerCode,
        CancellationToken ct = default
    );
}
