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

    public async Task<Result<TenantEmailAccount>> GetByTenantAndEmailAsync(
        Guid tenantId,
        string emailAddress,
        CancellationToken ct = default
    )
    {
        var normalized = emailAddress.Trim().ToLowerInvariant();
        var account = await dbContext
            .TenantEmailAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EmailAddress == normalized, ct);
        return account is null
            ? Result.Failure<TenantEmailAccount>(
                new Error("TenantEmailAccount.NotFound", $"No account with email '{emailAddress}' in this tenant.")
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

    public async Task<IReadOnlyList<TenantEmailAccount>> ListVisibleAsync(
        Guid tenantId,
        Guid userId,
        bool includeOffice,
        CancellationToken ct = default
    ) =>
        await dbContext
            .TenantEmailAccounts.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && (a.OwnerUserId == userId || (includeOffice && a.OwnerUserId == null)))
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantEmailAccount>> ListActiveAsync(CancellationToken ct = default) =>
        await dbContext
            .TenantEmailAccounts.IgnoreQueryFilters()
            .Where(a => a.Status == TenantEmailAccountStatus.Active)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

    // Tracked (sin AsNoTracking): el consumer de offboard los desconecta y persiste. IgnoreQueryFilters
    // porque corre system-level (el ITenantContext ambiente puede llegar vacío al scope del handler);
    // el WHERE ya acota por TenantId explícito. Index-backed por IX (TenantId, OwnerUserId).
    public async Task<IReadOnlyList<TenantEmailAccount>> ListByOwnerUserAsync(
        Guid tenantId,
        Guid ownerUserId,
        CancellationToken ct = default
    ) =>
        await dbContext
            .TenantEmailAccounts.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.OwnerUserId == ownerUserId)
            .ToListAsync(ct);

    // Mismo filtro que ListByOwnerUserAsync (buzones personales del usuario), solo cuenta — pre-flight.
    public Task<int> CountByOwnerUserAsync(Guid tenantId, Guid ownerUserId, CancellationToken ct = default) =>
        dbContext
            .TenantEmailAccounts.IgnoreQueryFilters()
            .CountAsync(a => a.TenantId == tenantId && a.OwnerUserId == ownerUserId, ct);
}
