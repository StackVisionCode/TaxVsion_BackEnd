using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Wallet;

namespace TaxVision.Wallet.Application.Activity;

/// <summary>Respuesta de <c>GET /wallet/transactions/{id}</c>.</summary>
public sealed record TransactionDetailView(
    LedgerEntryView Entry,
    string? ReferenceType,
    Guid? ReferenceId,
    string? Description,
    Guid? RequestedByUserId,
    int? PriceBookVersion,
    IReadOnlyList<ChannelChargeView> Channels,
    int? ReservedUnits,
    int? ConsumedUnits,
    IReadOnlyList<TransactionStepView> Steps,
    TopUpDetailView? TopUp
);

/// <summary>Canal × unidades reservadas × tarifa de la versión congelada.</summary>
public sealed record ChannelChargeView(string Channel, int Units, long UnitPriceMicros, long AmountMicros);

/// <summary>Un asiento de la misma referencia. El monto va sin signo: el movimiento dice la dirección.</summary>
public sealed record TransactionStepView(Guid EntryId, string Movement, long AmountMicros, DateTime CreatedAtUtc);

public sealed record TopUpDetailView(string? Provider, string? Method, bool Automatic, string Status);

/// <summary>Respuesta de <c>GET /wallet/summary</c>. Días cortados en la zona de la oficina.</summary>
public sealed record WalletSummaryView(
    string Currency,
    DateOnly From,
    DateOnly To,
    string TimeZone,
    long SpentMicros,
    long PreviousSpentMicros,
    IReadOnlyList<SpendBucketView> Buckets,
    IReadOnlyList<ChannelSpendView> ByChannel,
    long AverageDailySpendMicros,
    int? EstimatedDaysLeft
);

/// <summary>Gasto de un día, o de una semana ISO (la fecha es el lunes) si el rango pasa de 31 días.</summary>
public sealed record SpendBucketView(DateOnly Date, long SpentMicros);

/// <summary>Gasto por canal; "Other" cuando el asiento no trae unidades por canal (reservas anteriores al dato).</summary>
public sealed record ChannelSpendView(string Channel, long SpentMicros);

public static class WalletActivityErrors
{
    public static readonly Error TransactionNotFound = new("Wallet.TransactionNotFound", "Transaction not found.");
    public static readonly Error InvalidDateRange = new(
        "Wallet.InvalidDateRange",
        "Use yyyy-MM-dd or an ISO date-time, with 'from' before 'to' and at most 366 days."
    );
}
