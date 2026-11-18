using System.Globalization;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;

namespace TaxVision.Wallet.Application.Activity;

/// <summary>
/// Fechas de los filtros, rango semiabierto [desde, hasta). Un instante ISO (UTC si no trae zona) se usa tal cual:
/// "hasta" no entra. "yyyy-MM-dd" es un día de la oficina: como "hasta" incluye ese día (se corta en la medianoche
/// siguiente de su zona).
/// </summary>
public static class OfficeDates
{
    public static Result<(DateTime? FromUtc, DateTime? ToUtcExclusive)> ToUtcRange(
        string? from,
        string? to,
        ResolvedTimeZone zone
    )
    {
        DateTime? fromUtc = null;
        DateTime? toUtc = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (TryDay(from, out var day))
                fromUtc = LocalDay.StartUtc(day, zone);
            else if (TryInstant(from, out var instant))
                fromUtc = instant;
            else
                return Invalid();
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            if (TryDay(to, out var day))
                toUtc = LocalDay.StartUtc(day.AddDays(1), zone);
            else if (TryInstant(to, out var instant))
                toUtc = instant;
            else
                return Invalid();
        }

        return fromUtc >= toUtc ? Invalid() : Result.Success((fromUtc, toUtc));
    }

    /// <summary>
    /// Día de la oficina de un límite, o null si no vino. Con <paramref name="exclusiveEnd"/>, un instante "hasta" es
    /// exclusivo: su último día incluido es el del instante anterior (medianoche siguiente → ese mismo día).
    /// </summary>
    public static Result<DateOnly?> ToOfficeDay(string? value, ResolvedTimeZone zone, bool exclusiveEnd = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Success<DateOnly?>(null);
        if (TryDay(value, out var day))
            return Result.Success<DateOnly?>(day);
        if (!TryInstant(value, out var instant))
            return Result.Failure<DateOnly?>(WalletActivityErrors.InvalidDateRange);
        return Result.Success<DateOnly?>(CivilDate.Of(exclusiveEnd ? instant.AddTicks(-1) : instant, zone));
    }

    private static bool TryDay(string value, out DateOnly day) =>
        DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    private static bool TryInstant(string value, out DateTime utc)
    {
        var parsed = DateTimeOffset.TryParse(
            value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var offset
        );
        utc = parsed ? offset.UtcDateTime : default;
        return parsed;
    }

    private static Result<(DateTime?, DateTime?)> Invalid() =>
        Result.Failure<(DateTime?, DateTime?)>(WalletActivityErrors.InvalidDateRange);
}
