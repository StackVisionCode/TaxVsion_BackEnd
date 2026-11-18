using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Application.Settings.Abstractions;
using TaxVision.Wallet.Domain.Settings;

namespace TaxVision.Wallet.Infrastructure.Persistence.Repositories;

/// <summary>Ajustes del monedero e historia. Filtra por tenant explícito (mismo criterio que WalletRepository).</summary>
public sealed class WalletSettingsRepository(WalletDbContext db) : IWalletSettingsRepository
{
    public Task<WalletSettings?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        db.WalletSettings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public async Task AddAsync(WalletSettings settings, CancellationToken ct = default) =>
        await db.WalletSettings.AddAsync(settings, ct);

    public async Task AddChangeAsync(WalletSettingsChange change, CancellationToken ct = default) =>
        await db.WalletSettingsChanges.AddAsync(change, ct);

    public async Task<IReadOnlyList<WalletSettingsChange>> ListChangesAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    ) =>
        await db
            .WalletSettingsChanges.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.AtUtc)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
}
