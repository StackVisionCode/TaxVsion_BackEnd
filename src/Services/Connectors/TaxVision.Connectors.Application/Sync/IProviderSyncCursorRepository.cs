using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Application.Sync;

public interface IProviderSyncCursorRepository
{
    Task AddAsync(ProviderSyncCursor cursor, CancellationToken ct = default);

    Task<Result<ProviderSyncCursor>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);
}
