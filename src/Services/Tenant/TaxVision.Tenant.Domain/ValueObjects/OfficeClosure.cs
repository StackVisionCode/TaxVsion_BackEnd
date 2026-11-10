using System.Globalization;
using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.Profiles;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// Un día (o tramo de días) en que la oficina cierra: feriados y días no laborables. Las fechas son
/// del calendario de la oficina, sin hora. <see cref="RepeatsYearly"/> lo repite el mismo día cada año.
/// </summary>
public sealed class OfficeClosure
{
    public const string DateFormat = "yyyy-MM-dd";
    public const int MaxLabelLength = 60;
    public const int MaxDays = 31;

    public static readonly Error Invalid = new(
        "Tenant.Profile.Closures",
        "Each closure needs a label and a date (yyyy-MM-dd); an end date must not come before it and the range can't exceed 31 days."
    );

    // EF Core materializa los cierres guardados sin volver a validarlos.
    private OfficeClosure() { }

    public DateOnly Date { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string Label { get; private set; } = default!;
    public bool RepeatsYearly { get; private set; }

    public static Result<OfficeClosure> Create(OfficeClosureDraft draft)
    {
        var label = draft.Label?.Trim();
        if (string.IsNullOrEmpty(label) || label.Length > MaxLabelLength || !TryParse(draft.Date, out var date))
            return Result.Failure<OfficeClosure>(Invalid);

        DateOnly? end = null;
        if (!string.IsNullOrWhiteSpace(draft.EndDate))
        {
            if (
                !TryParse(draft.EndDate, out var parsed)
                || parsed < date
                || parsed.DayNumber - date.DayNumber >= MaxDays
            )
                return Result.Failure<OfficeClosure>(Invalid);
            end = parsed == date ? null : parsed;
        }

        return Result.Success(
            new OfficeClosure
            {
                Date = date,
                EndDate = end,
                Label = label,
                RepeatsYearly = draft.RepeatsYearly,
            }
        );
    }

    public bool Matches(OfficeClosure other) =>
        Date == other.Date && EndDate == other.EndDate && Label == other.Label && RepeatsYearly == other.RepeatsYearly;

    private static bool TryParse(string? raw, out DateOnly date) =>
        DateOnly.TryParseExact(raw?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>Los cierres de la oficina, ordenados por fecha. Vacío = ninguno.</summary>
public sealed class OfficeClosures
{
    public const int MaxCount = 60;

    public static readonly OfficeClosures None = new([]);

    public static readonly Error TooMany = new(
        "Tenant.Profile.Closures",
        $"An office can list up to {MaxCount} closures."
    );

    private readonly List<OfficeClosure> _items;

    private OfficeClosures(List<OfficeClosure> items) => _items = items;

    public IReadOnlyList<OfficeClosure> Items => _items;

    public static Result<OfficeClosures> Create(IReadOnlyList<OfficeClosureDraft> drafts)
    {
        if (drafts.Count == 0)
            return Result.Success(None);
        if (drafts.Count > MaxCount)
            return Result.Failure<OfficeClosures>(TooMany);

        var items = new List<OfficeClosure>(drafts.Count);
        foreach (var draft in drafts)
        {
            var closure = OfficeClosure.Create(draft);
            if (closure.IsFailure)
                return Result.Failure<OfficeClosures>(closure.Error);
            items.Add(closure.Value);
        }

        items.Sort((left, right) => left.Date.CompareTo(right.Date));
        return Result.Success(new OfficeClosures(items));
    }

    public bool Matches(IReadOnlyList<OfficeClosure> current)
    {
        if (current.Count != _items.Count)
            return false;
        for (var i = 0; i < _items.Count; i++)
        {
            if (!_items[i].Matches(current[i]))
                return false;
        }
        return true;
    }
}
