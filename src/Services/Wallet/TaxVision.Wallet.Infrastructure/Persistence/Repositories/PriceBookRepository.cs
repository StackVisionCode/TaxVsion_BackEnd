using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Domain.Pricing;

namespace TaxVision.Wallet.Infrastructure.Persistence.Repositories;

/// <summary>Catálogo de precios global (no tenant-owned). La versión vigente = mayor Version con
/// EffectiveFromUtc ≤ ahora.</summary>
public sealed class PriceBookRepository(WalletDbContext db) : IPriceBookRepository
{
    public Task<PriceBookVersion?> GetActiveVersionAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return db
            .PriceBookVersions.Include(v => v.Rules)
            .Where(v => v.EffectiveFromUtc <= now)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> GetMaxVersionAsync(CancellationToken ct = default) =>
        await db.PriceBookVersions.MaxAsync(v => (int?)v.Version, ct) ?? 0;

    public async Task AddVersionAsync(PriceBookVersion version, CancellationToken ct = default) =>
        await db.PriceBookVersions.AddAsync(version, ct);
}
