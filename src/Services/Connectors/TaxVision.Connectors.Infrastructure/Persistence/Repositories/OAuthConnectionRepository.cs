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

    public async Task<Result<OAuthConnection>> GetByAccountIdReadOnlyAsync(
        Guid accountId,
        CancellationToken ct = default
    )
    {
        var connection = await dbContext
            .OAuthConnections.AsNoTracking()
            .Include(c => c.Token)
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
        // Job sin tenant en contexto: IgnoreQueryFilters explícito, igual que TenantEmailAccountRepository.
        var refreshableAccounts = dbContext
            .TenantEmailAccounts.IgnoreQueryFilters()
            .Where(a => a.Status == TenantEmailAccountStatus.Active || a.Status == TenantEmailAccountStatus.Connected);

        return await dbContext
            .OAuthConnections.Where(c =>
                c.Status == OAuthConnectionStatus.Active && c.Token!.AccessTokenExpiresAtUtc < thresholdUtc
            )
            .Join(refreshableAccounts, c => c.AccountId, a => a.Id, (c, _) => c.AccountId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ListActiveScopesByAccountIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default
    ) =>
        await dbContext
            .OAuthConnections.AsNoTracking()
            .Where(c => accountIds.Contains(c.AccountId) && c.Status == OAuthConnectionStatus.Active)
            .ToDictionaryAsync(c => c.AccountId, c => c.Scope, ct);

    // El token es tabla aparte sin FK con cascade (ver ConnectorsDbContext): se borra explícito.
    public void Remove(OAuthConnection connection)
    {
        if (connection.Token is not null)
            dbContext.OAuthTokens.Remove(connection.Token);
        dbContext.OAuthConnections.Remove(connection);
    }
}
