using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Infrastructure.Persistence.Repositories;

public sealed class TenantEmailAccountRepository(ConnectorsDbContext dbContext) : ITenantEmailAccountRepository
{
    public async Task AddAsync(TenantEmailAccount account, CancellationToken ct = default) =>
        await dbContext.TenantEmailAccounts.AddAsync(account, ct);

    // RBAC Fase 5 — deliberadamente cross-tenant (ver doc de la interfaz): background
    // jobs/webhooks system-level no tienen tenant en contexto. IgnoreQueryFilters() explícito;
    // los 2 call sites autenticados (GetTenantEmailAccountHandler/DisconnectAccountHandler) ya
    // hacen su propio chequeo explícito account.TenantId == caller's tenantId después de esto.
    public async Task<Result<TenantEmailAccount>> GetByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await dbContext
            .TenantEmailAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);
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
        var account = await dbContext
            .TenantEmailAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.EmailAddress == normalized, ct);
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
            .TenantEmailAccounts.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantEmailAccount>> ListActiveAsync(CancellationToken ct = default) =>
        await dbContext
            .TenantEmailAccounts.IgnoreQueryFilters()
            .Where(a => a.Status == TenantEmailAccountStatus.Active)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
}
