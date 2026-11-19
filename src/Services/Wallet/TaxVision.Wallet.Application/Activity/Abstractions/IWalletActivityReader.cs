using BuildingBlocks.Common;
using TaxVision.Wallet.Domain.Pricing;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Activity.Abstractions;

/// <summary>Filtro de <c>GET /wallet/transactions</c>.</summary>
public enum TransactionKind
{
    All = 0,

    /// <summary>TopUp + Reward + Adjustment positivo.</summary>
    TopUps = 1,

    /// <summary>Consume + UsageRefund + RewardReversal + Adjustment negativo.</summary>
    Charges = 2,

    /// <summary>Reserve + Release.</summary>
    Reserved = 3,
}

/// <summary>Rango en UTC: desde inclusivo, hasta exclusivo.</summary>
public sealed record TransactionFilter(TransactionKind Kind, DateTime? FromUtc, DateTime? ToUtcExclusive);

/// <summary>Asiento con su reserva (si vino de una) o su orden de recarga (si es un TopUp).</summary>
public sealed record LedgerEntryRow(LedgerEntry Entry, WalletReservation? Reservation, WalletTopUp? TopUp);

/// <summary>Consumo o devolución para el resumen, con las unidades de su reserva si se conoce.</summary>
public sealed record SpendRow(
    DateTime CreatedAtUtc,
    MovementType Movement,
    long DeltaPostedMicros,
    int? PriceBookVersion,
    ReservationChannelUnits? Units
)
{
    /// <summary>Gastado = Consume − UsageRefund.</summary>
    public long SpentMicros => -DeltaPostedMicros;
}

/// <summary>Lecturas sin seguimiento del historial del monedero (listado, detalle y resumen).</summary>
public interface IWalletActivityReader
{
    /// <summary>Asientos del tenant, más recientes primero.</summary>
    Task<PagedResult<LedgerEntryRow>> ListAsync(
        Guid tenantId,
        TransactionFilter filter,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task<LedgerEntryRow?> GetAsync(Guid tenantId, Guid entryId, CancellationToken ct = default);

    /// <summary>Todos los asientos de una referencia, en orden.</summary>
    Task<IReadOnlyList<LedgerEntry>> ListByReferenceAsync(
        Guid tenantId,
        string? referenceType,
        Guid referenceId,
        CancellationToken ct = default
    );

    /// <summary>Consume y UsageRefund del rango [desde, hasta).</summary>
    Task<IReadOnlyList<SpendRow>> ListSpendAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        CancellationToken ct = default
    );

    /// <summary>Tarifa por canal de cada versión pedida del catálogo.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyDictionary<PriceChannel, long>>> GetUnitPricesAsync(
        IReadOnlyCollection<int> versions,
        CancellationToken ct = default
    );
}
