using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Domain.Pricing;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas sin seguimiento del historial: cada asiento con su reserva (misma referencia) o su orden de recarga
/// (el TopUp guarda el id de la orden en <c>ReferenceId</c>). Filtra por tenant explícito.
/// </summary>
public sealed class WalletActivityReader(WalletDbContext db) : IWalletActivityReader
{
    public async Task<PagedResult<LedgerEntryRow>> ListAsync(
        Guid tenantId,
        TransactionFilter filter,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var entries = Filter(Ledger(tenantId), filter);
        var total = await entries.CountAsync(ct);
        var paged = entries
            .OrderByDescending(e => e.CreatedAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * size)
            .Take(size);
        var rows = await WithSources(tenantId, paged).ToListAsync(ct);
        var items = rows.OrderByDescending(r => r.Entry.CreatedAtUtc)
            .ThenByDescending(r => r.Entry.Id)
            .Select(r => r.ToRow())
            .ToList();
        return new PagedResult<LedgerEntryRow>(items, page, size, total);
    }

    public async Task<LedgerEntryRow?> GetAsync(Guid tenantId, Guid entryId, CancellationToken ct = default)
    {
        var row = await WithSources(tenantId, Ledger(tenantId).Where(e => e.Id == entryId)).FirstOrDefaultAsync(ct);
        return row?.ToRow();
    }

    public async Task<IReadOnlyList<LedgerEntry>> ListByReferenceAsync(
        Guid tenantId,
        string? referenceType,
        Guid referenceId,
        CancellationToken ct = default
    )
    {
        var query = Ledger(tenantId).Where(e => e.ReferenceId == referenceId);
        if (referenceType is not null)
            query = query.Where(e => e.ReferenceType == referenceType);
        return await query.OrderBy(e => e.CreatedAtUtc).ThenBy(e => e.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SpendRow>> ListSpendAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        CancellationToken ct = default
    )
    {
        var rows = await (
            from e in Ledger(tenantId)
            where
                (e.Movement == MovementType.Consume || e.Movement == MovementType.UsageRefund)
                && e.CreatedAtUtc >= fromUtc
                && e.CreatedAtUtc < toUtcExclusive
            join r0 in Reservations(tenantId)
                on new { Type = e.ReferenceType, Id = e.ReferenceId } equals new
                {
                    Type = (string?)r0.ReferenceType,
                    Id = (Guid?)r0.ReferenceId,
                }
                into reservations
            from r in reservations.DefaultIfEmpty()
            select new
            {
                e.CreatedAtUtc,
                e.Movement,
                e.DeltaPostedMicros,
                Version = (int?)r!.PriceBookVersion,
                Email = (int?)r.EmailUnits,
                Sms = (int?)r.SmsUnits,
                Push = (int?)r.PushUnits,
                WhatsApp = (int?)r.WhatsAppUnits,
            }
        ).ToListAsync(ct);

        return rows.Select(r => new SpendRow(
                r.CreatedAtUtc,
                r.Movement,
                r.DeltaPostedMicros,
                r.Version,
                r.Version is null
                    ? null
                    : new ReservationChannelUnits(r.Email ?? 0, r.Sms ?? 0, r.Push ?? 0, r.WhatsApp ?? 0)
            ))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<PriceChannel, long>>> GetUnitPricesAsync(
        IReadOnlyCollection<int> versions,
        CancellationToken ct = default
    )
    {
        var rules = await (
            from v in db.PriceBookVersions.AsNoTracking()
            where versions.Contains(v.Version)
            join rule in db.PriceRules.AsNoTracking() on v.Id equals rule.PriceBookVersionId
            select new
            {
                v.Version,
                rule.Channel,
                rule.UnitPriceMicros,
            }
        ).ToListAsync(ct);
        return rules
            .GroupBy(r => r.Version)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<PriceChannel, long>)g.ToDictionary(r => r.Channel, r => r.UnitPriceMicros)
            );
    }

    private IQueryable<LedgerEntry> Ledger(Guid tenantId) =>
        db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == tenantId);

    private IQueryable<WalletReservation> Reservations(Guid tenantId) =>
        db.Reservations.IgnoreQueryFilters().AsNoTracking().Where(r => r.TenantId == tenantId);

    private IQueryable<SourcedEntry> WithSources(Guid tenantId, IQueryable<LedgerEntry> entries) =>
        from e in entries
        join r0 in Reservations(tenantId)
            on new { Type = e.ReferenceType, Id = e.ReferenceId } equals new
            {
                Type = (string?)r0.ReferenceType,
                Id = (Guid?)r0.ReferenceId,
            }
            into reservations
        from r in reservations.DefaultIfEmpty()
        join t0 in db.WalletTopUps.IgnoreQueryFilters().AsNoTracking().Where(t => t.TenantId == tenantId)
            on e.ReferenceId equals (Guid?)t0.Id
            into topUps
        from t in topUps.DefaultIfEmpty()
        select new SourcedEntry
        {
            Entry = e,
            Reservation = r,
            TopUp = t,
        };

    private static IQueryable<LedgerEntry> Filter(IQueryable<LedgerEntry> query, TransactionFilter filter)
    {
        if (filter.FromUtc is { } from)
            query = query.Where(e => e.CreatedAtUtc >= from);
        if (filter.ToUtcExclusive is { } to)
            query = query.Where(e => e.CreatedAtUtc < to);
        return filter.Kind switch
        {
            TransactionKind.TopUps => query.Where(e =>
                e.Movement == MovementType.TopUp
                || e.Movement == MovementType.Reward
                || (e.Movement == MovementType.Adjustment && e.DeltaPostedMicros > 0)
            ),
            TransactionKind.Charges => query.Where(e =>
                e.Movement == MovementType.Consume
                || e.Movement == MovementType.UsageRefund
                || e.Movement == MovementType.RewardReversal
                || (e.Movement == MovementType.Adjustment && e.DeltaPostedMicros < 0)
            ),
            TransactionKind.Reserved => query.Where(e =>
                e.Movement == MovementType.Reserve || e.Movement == MovementType.Release
            ),
            _ => query,
        };
    }

    private sealed class SourcedEntry
    {
        public required LedgerEntry Entry { get; init; }
        public WalletReservation? Reservation { get; init; }
        public WalletTopUp? TopUp { get; init; }

        // Solo un TopUp apunta a su orden de recarga.
        public LedgerEntryRow ToRow() => new(Entry, Reservation, Entry.Movement == MovementType.TopUp ? TopUp : null);
    }
}
