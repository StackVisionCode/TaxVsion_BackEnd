using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Infrastructure.Persistence.Repositories;

public sealed class ProviderSyncCursorRepository(ConnectorsDbContext dbContext) : IProviderSyncCursorRepository
{
    public async Task AddAsync(ProviderSyncCursor cursor, CancellationToken ct = default) =>
        await dbContext.ProviderSyncCursors.AddAsync(cursor, ct);

    public async Task<Result<ProviderSyncCursor>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var cursor = await dbContext.ProviderSyncCursors.FirstOrDefaultAsync(c => c.AccountId == accountId, ct);
        return cursor is null
            ? Result.Failure<ProviderSyncCursor>(
                new Error("ProviderSyncCursor.NotFound", $"ProviderSyncCursor for account {accountId} not found.")
            )
            : Result.Success(cursor);
    }
}
