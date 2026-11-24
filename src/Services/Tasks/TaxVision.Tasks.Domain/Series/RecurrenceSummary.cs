namespace TaxVision.Tasks.Domain.Series;

/// <summary>
/// La regla dicha en inglés, para las formas que emite el constructor ("Every quarter on the last day of Jan, Apr, Jul
/// and Oct"). Cualquier otra cosa es "Custom schedule": se muestra con sus fechas, pero el constructor no la edita.
/// </summary>
public static class RecurrenceSummary
{
    public const string Custom = "Custom schedule";

    private static readonly string[] Known = ["FREQ", "INTERVAL", "BYDAY", "BYMONTHDAY", "BYMONTH"];

    private static readonly Dictionary<string, string> Days = new()
    {
        ["MO"] = "Monday",
        ["TU"] = "Tuesday",
        ["WE"] = "Wednesday",
        ["TH"] = "Thursday",
        ["FR"] = "Friday",
        ["SA"] = "Saturday",
        ["SU"] = "Sunday",
    };

    private static readonly string[] MonthNames =
    [
        "January",
        "February",
        "March",
        "April",
        "May",
        "June",
        "July",
        "August",
        "September",
        "October",
        "November",
        "December",
    ];

    private static readonly string[] Ordinals = ["", "first", "second", "third", "fourth"];

    public static string Describe(string rule, RecurrenceMode mode)
    {
        var parts = Parse(rule);
        if (parts is null || !parts.TryGetValue("FREQ", out var freq))
            return Custom;
        var every = parts.TryGetValue("INTERVAL", out var raw) && int.TryParse(raw, out var n) && n > 0 ? n : 1;
        var byDay = parts.GetValueOrDefault("BYDAY");
        var byMonthDay = parts.GetValueOrDefault("BYMONTHDAY");
        var byMonth = parts.GetValueOrDefault("BYMONTH");

        if (mode == RecurrenceMode.AfterCompletion)
            return byDay is null && byMonthDay is null && byMonth is null && Unit(freq, every) is { } unit
                ? $"{every} {unit} after the previous one is completed"
                : Custom;

        return freq switch
            {
                "DAILY" when byDay is null && byMonthDay is null && byMonth is null => every == 1
                    ? "Every day"
                    : $"Every {every} days",
                "WEEKLY" when byMonthDay is null && byMonth is null => Weekly(every, byDay),
                "MONTHLY" when byMonth is null => Monthly(every, byDay, byMonthDay),
                "YEARLY" when every == 1 && byDay is null && byMonth is not null && byMonthDay is not null => Yearly(
                    byMonth,
                    byMonthDay
                ),
                _ => Custom,
            } ?? Custom;
    }

    private static string? Weekly(int every, string? byDay)
    {
        var prefix = every == 1 ? "Every week" : $"Every {every} weeks";
        if (byDay is null)
            return prefix;
        var codes = byDay.Split(',');
        if (!codes.All(Days.ContainsKey))
            return null;
        if (every == 1 && codes.Order().SequenceEqual(["FR", "MO", "TH", "TU", "WE"]))
            return "Every weekday";
        return $"{prefix} on {Join([.. codes.Select(c => Days[c])])}";
    }

    private static string? Monthly(int every, string? byDay, string? byMonthDay)
    {
        var prefix = every == 1 ? "Every month" : $"Every {every} months";
        if (byDay is null && byMonthDay is null)
            return prefix;
        if (byDay is null)
            return DayOfMonth(byMonthDay!) is { } day ? $"{prefix} on {day}" : null;
        if (byMonthDay is not null || byDay.Contains(','))
            return null;
        var weekday = byDay[^2..];
        if (!Days.TryGetValue(weekday, out var name) || !int.TryParse(byDay[..^2], out var position))
            return null;
        var ordinal =
            position == -1 ? "last"
            : position is >= 1 and <= 4 ? Ordinals[position]
            : null;
        return ordinal is null ? null : $"{prefix} on the {ordinal} {name}";
    }

    private static string? Yearly(string byMonth, string byMonthDay)
    {
        var months = byMonth.Split(',').Select(m => int.TryParse(m, out var v) ? v : 0).ToArray();
        if (months.Any(m => m is < 1 or > 12) || byMonthDay.Contains(','))
            return null;
        if (months.Length == 1)
        {
            var month = MonthNames[months[0] - 1];
            if (byMonthDay == "-1")
                return $"Every year on the last day of {month}";
            return int.TryParse(byMonthDay, out var d) && d is >= 1 and <= 29 ? $"Every year on {month} {d}" : null;
        }
        if (DayOfMonth(byMonthDay) is not { } day)
            return null;
        var list = Join([.. months.Select(m => MonthNames[m - 1][..3])]);
        var quarterly = months.Length == 4 && months.Zip(months.Skip(1), (a, b) => b - a).All(gap => gap == 3);
        return quarterly ? $"Every quarter on {day} of {list}" : $"Every year on {day} of {list}";
    }

    /// <summary>"the 15th" o "the last day"; los días 29 a 31 no son del constructor.</summary>
    private static string? DayOfMonth(string value)
    {
        if (value == "-1")
            return "the last day";
        return int.TryParse(value, out var d) && d is >= 1 and <= 28 ? $"the {d}{Suffix(d)}" : null;
    }

    private static string Suffix(int d) =>
        d is >= 11 and <= 13
            ? "th"
            : (d % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };

    private static string? Unit(string freq, int every) =>
        freq switch
        {
            "DAILY" => every == 1 ? "day" : "days",
            "WEEKLY" => every == 1 ? "week" : "weeks",
            "MONTHLY" => every == 1 ? "month" : "months",
            _ => null,
        };

    private static string Join(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    private static Dictionary<string, string>? Parse(string rule)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in rule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !Known.Contains(pair[0].ToUpperInvariant()))
                return null;
            parts[pair[0].ToUpperInvariant()] = pair[1].ToUpperInvariant();
        }
        return parts;
    }
}
