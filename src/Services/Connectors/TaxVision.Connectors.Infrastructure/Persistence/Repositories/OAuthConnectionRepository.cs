using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.Persistence.Repositories;

public sealed class OAuthConnectionRepository(ConnectorsDbContext dbContext) : IOAuthConnectionRepository
{
    public async Task AddAsync(OAuthConnection connection, CancellationToken ct = default) =>
        await dbContext.OAuthConnections.AddAsync(connection, ct);

    public async Task<Result<OAuthConnection>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var connection = await dbContext
            .OAuthConnections.Include(c => c.Token)
            .FirstOrDefaultAsync(c => c.AccountId == accountId, ct);

        return connection is null
            ? Result.Failure<OAuthConnection>(
                new Error("OAuthConnection.NotFound", $"OAuthConnection for account {accountId} not found.")
            )
            : Result.Success(connection);
    }

    public async Task<IReadOnlyList<Guid>> ListAccountIdsWithTokenExpiringBeforeAsync(
        DateTime thresholdUtc,
        CancellationToken ct = default
    )
    {
        return await dbContext
            .OAuthConnections.Where(c =>
                c.Status == OAuthConnectionStatus.Active && c.Token!.AccessTokenExpiresAtUtc < thresholdUtc
            )
            .Select(c => c.AccountId)
            .ToListAsync(ct);
    }
}
