using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>Errores de <see cref="ValueObjects.EventTiming"/>.</summary>
public static class TimingErrors
{
    public static readonly Error EndBeforeStart = new(
        "Calendar.Timing.EndBeforeStart",
        "The end of the event must come after its start."
    );

    public static readonly Error InvalidTimeZone = new(
        "Calendar.Timing.InvalidTimeZone",
        "The time zone must be a valid IANA identifier."
    );

    public static readonly Error RecurringMustBeLocal = new(
        "Calendar.Timing.RecurringMustBeLocal",
        "A recurring event is stored as wall-clock time plus its time zone, never as UTC."
    );

    public static readonly Error AllDayHasTime = new(
        "Calendar.Timing.AllDayHasTime",
        "An all-day event is a date, not an instant."
    );

    public static readonly Error DurationTooLong = new(
        "Calendar.Timing.DurationTooLong",
        "An event cannot last more than 30 days."
    );

    public static readonly Error NotUtc = new(
        "Calendar.Timing.NotUtc",
        "A point-in-time event must carry both ends as UTC."
    );

    public static readonly Error InvalidLocalTime = new(
        "Calendar.Timing.InvalidLocalTime",
        "That wall-clock time does not exist on that date in that time zone: the clock jumps over it."
    );
}
