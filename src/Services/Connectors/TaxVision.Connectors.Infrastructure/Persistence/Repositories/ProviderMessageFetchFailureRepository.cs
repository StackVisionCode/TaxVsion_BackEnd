using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Infrastructure.Persistence.Repositories;

public sealed class ProviderMessageFetchFailureRepository(ConnectorsDbContext dbContext)
    : IProviderMessageFetchFailureRepository
{
    public async Task<IReadOnlyList<ProviderMessageFetchFailure>> ListPendingAsync(
        Guid accountId,
        CancellationToken ct = default
    ) =>
        await dbContext
            .ProviderMessageFetchFailures.Where(f =>
                f.AccountId == accountId && f.Status == ProviderMessageFetchFailureStatus.Pending
            )
            .ToListAsync(ct);

    public Task<ProviderMessageFetchFailure?> GetAsync(
        Guid accountId,
        string providerMessageId,
        CancellationToken ct = default
    ) =>
        dbContext.ProviderMessageFetchFailures.FirstOrDefaultAsync(
            f => f.AccountId == accountId && f.ProviderMessageId == providerMessageId,
            ct
        );

    public async Task AddAsync(ProviderMessageFetchFailure failure, CancellationToken ct = default) =>
        await dbContext.ProviderMessageFetchFailures.AddAsync(failure, ct);
}
