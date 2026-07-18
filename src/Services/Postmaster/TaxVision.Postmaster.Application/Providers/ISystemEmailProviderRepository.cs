using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Application.Providers;

public interface ISystemEmailProviderRepository
{
    Task AddAsync(SystemEmailProvider provider, CancellationToken ct = default);

    Task<Result<SystemEmailProvider>> GetByCodeAsync(string providerCode, CancellationToken ct = default);

    /// <summary>El único provider System habilitado usado como default (Fase 3: resolución no distingue tenant).</summary>
    Task<Result<SystemEmailProvider>> GetEnabledDefaultAsync(CancellationToken ct = default);
}
