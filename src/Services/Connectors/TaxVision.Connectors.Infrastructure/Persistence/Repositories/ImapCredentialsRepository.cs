using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.Persistence.Repositories;

public sealed class ImapCredentialsRepository(ConnectorsDbContext dbContext) : IImapCredentialsRepository
{
    public async Task AddAsync(ImapCredentials credentials, CancellationToken ct = default) =>
        await dbContext.ImapCredentials.AddAsync(credentials, ct);

    public async Task<Result<ImapCredentials>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var credentials = await dbContext.ImapCredentials.FirstOrDefaultAsync(c => c.AccountId == accountId, ct);
        return credentials is null
            ? Result.Failure<ImapCredentials>(
                new Error("ImapCredentials.NotFound", $"ImapCredentials for account {accountId} not found.")
            )
            : Result.Success(credentials);
    }
}
