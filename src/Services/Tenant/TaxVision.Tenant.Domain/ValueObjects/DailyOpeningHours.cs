using System.Globalization;
using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>Un día de atención: apertura y cierre <c>HH:mm</c> en la zona horaria de la oficina.</summary>
public sealed class DailyOpeningHours
{
    public const string TimeFormat = "HH:mm";

    public static readonly Error InvalidFormat = new(
        "Tenant.Profile.BusinessHours",
        "Business hours need a day name (Monday to Sunday) and times in HH:mm format, like 09:00."
    );

    public static readonly Error ClosesBeforeOpening = new(
        "Tenant.Profile.BusinessHours",
        "The closing time must be later than the opening time."
    );

    // EF Core materializa el horario guardado sin volver a validarlo.
    private DailyOpeningHours() { }

    public DayOfWeek Day { get; private set; }
    public TimeOnly Opens { get; private set; }
    public TimeOnly Closes { get; private set; }

    public static Result<DailyOpeningHours> Create(string? day, string? opens, string? closes)
    {
        if (
            !TryParseDay(day, out var parsedDay)
            || !TryParseTime(opens, out var opening)
            || !TryParseTime(closes, out var closing)
        )
            return Result.Failure<DailyOpeningHours>(InvalidFormat);

        if (closing <= opening)
            return Result.Failure<DailyOpeningHours>(ClosesBeforeOpening);

        return Result.Success(
            new DailyOpeningHours
            {
                Day = parsedDay,
                Opens = opening,
                Closes = closing,
            }
        );
    }

    /// <summary>Lunes primero, domingo último (semana ISO), para mostrar y comparar siempre igual.</summary>
    public int IsoOrder => ((int)Day + 6) % 7;

    public bool Matches(DailyOpeningHours other) => Day == other.Day && Opens == other.Opens && Closes == other.Closes;

    private static bool TryParseDay(string? raw, out DayOfWeek day)
    {
        day = default;
        var trimmed = raw?.Trim();
        // Enum.TryParse acepta números ("3"): solo valen los nombres.
        if (string.IsNullOrEmpty(trimmed) || char.IsAsciiDigit(trimmed[0]) || trimmed[0] == '-')
            return false;
        return Enum.TryParse(trimmed, ignoreCase: true, out day) && Enum.IsDefined(day);
    }

    private static bool TryParseTime(string? raw, out TimeOnly time) =>
        TimeOnly.TryParseExact(raw?.Trim(), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
