using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.Persistence.Repositories;

public sealed class SmtpCredentialsRepository(ConnectorsDbContext dbContext) : ISmtpCredentialsRepository
{
    public async Task AddAsync(SmtpCredentials credentials, CancellationToken ct = default) =>
        await dbContext.SmtpCredentials.AddAsync(credentials, ct);

    public async Task<Result<SmtpCredentials>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var credentials = await dbContext.SmtpCredentials.FirstOrDefaultAsync(c => c.AccountId == accountId, ct);
        return credentials is null
            ? Result.Failure<SmtpCredentials>(
                new Error("SmtpCredentials.NotFound", $"SmtpCredentials for account {accountId} not found.")
            )
            : Result.Success(credentials);
    }
}
