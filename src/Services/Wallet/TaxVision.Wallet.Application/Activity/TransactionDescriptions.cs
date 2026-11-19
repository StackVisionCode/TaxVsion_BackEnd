using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Domain.Pricing;
using TaxVision.Wallet.Domain.Reservations;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Application.Activity;

/// <summary>Qué fue cada asiento, para mostrarlo sin otra llamada (copy en inglés).</summary>
public static class TransactionDescriptions
{
    public static string? For(LedgerEntryRow row) =>
        row.TopUp is { } topUp
            ? ForTopUp(topUp)
            : row.Reservation?.Description ?? Fallback(row.Entry.ReferenceType, row.Entry.Movement);

    public static string ForTopUp(WalletTopUp topUp)
    {
        var name = topUp.Automatic ? "Automatic top-up" : "Top-up";
        return topUp.PaymentLabel is { } label ? $"{name} · {label}" : name;
    }

    /// <summary>Unidades del asiento: reservadas, usadas o devueltas según el movimiento.</summary>
    public static int? UnitsFor(LedgerEntry entry, WalletReservation? reservation) =>
        reservation is null
            ? null
            : entry.Movement switch
            {
                MovementType.Reserve => reservation.ReservedUnits,
                MovementType.Consume => reservation.ConsumedUnits,
                MovementType.Release => reservation.ReservedUnits - (reservation.ConsumedUnits ?? 0),
                _ => null,
            };

    // Sin descripción del consumidor: un texto por tipo (contrato §8.1).
    private static string? Fallback(string? referenceType, MovementType movement) =>
        movement switch
        {
            MovementType.TopUp => "Top-up",
            MovementType.Reward => "Reward",
            MovementType.RewardReversal => "Reward reversal",
            MovementType.Adjustment => "Adjustment",
            _ when referenceType?.Contains("campaign", StringComparison.OrdinalIgnoreCase) == true => "Campaign",
            _ when referenceType?.Contains("sms", StringComparison.OrdinalIgnoreCase) == true => "SMS",
            _ => null,
        };
}

/// <summary>Reparte un gasto entre canales según el costo de cada uno en su reserva (unidades × tarifa).</summary>
public static class ChannelSpend
{
    public const string Other = "Other";

    public static IReadOnlyList<(string Channel, long Micros)> Split(
        long micros,
        ReservationChannelUnits? units,
        IReadOnlyDictionary<PriceChannel, long>? prices
    )
    {
        var costs = (units?.NonZero() ?? [])
            .Select(c => (c.Channel, Cost: c.Units * (prices?.GetValueOrDefault(c.Channel) ?? 0)))
            .Where(c => c.Cost > 0)
            .ToList();
        var total = costs.Sum(c => c.Cost);
        if (total == 0)
            return [(Other, micros)];

        var shares = costs
            .Select(c => (Channel: c.Channel.ToString(), Micros: (long)((decimal)micros * c.Cost / total)))
            .ToList();
        // El resto del redondeo va al canal más caro: la suma siempre cuadra.
        var largest = costs.IndexOf(costs.MaxBy(c => c.Cost));
        shares[largest] = (shares[largest].Channel, shares[largest].Micros + micros - shares.Sum(s => s.Micros));
        return shares;
    }
}
