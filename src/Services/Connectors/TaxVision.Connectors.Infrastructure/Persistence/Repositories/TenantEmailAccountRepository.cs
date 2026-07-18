using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.Persistence.Repositories;

public sealed class TenantEmailAccountRepository(ConnectorsDbContext dbContext) : ITenantEmailAccountRepository
{
    public async Task AddAsync(TenantEmailAccount account, CancellationToken ct = default) =>
        await dbContext.TenantEmailAccounts.AddAsync(account, ct);

    public async Task<Result<TenantEmailAccount>> GetByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await dbContext.TenantEmailAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        return account is null
            ? Result.Failure<TenantEmailAccount>(
                new Error("TenantEmailAccount.NotFound", $"TenantEmailAccount {accountId} not found.")
            )
            : Result.Success(account);
    }

    public async Task<Result<TenantEmailAccount>> GetByEmailAddressAsync(
        string emailAddress,
        CancellationToken ct = default
    )
    {
        var normalized = emailAddress.Trim().ToLowerInvariant();
        var account = await dbContext.TenantEmailAccounts.FirstOrDefaultAsync(a => a.EmailAddress == normalized, ct);
        return account is null
            ? Result.Failure<TenantEmailAccount>(
                new Error("TenantEmailAccount.NotFound", $"TenantEmailAccount with email '{emailAddress}' not found.")
            )
            : Result.Success(account);
    }

    public async Task<IReadOnlyList<TenantEmailAccount>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await dbContext
            .TenantEmailAccounts.Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantEmailAccount>> ListActiveAsync(CancellationToken ct = default) =>
        await dbContext
            .TenantEmailAccounts.Where(a => a.Status == TenantEmailAccountStatus.Active)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
}
