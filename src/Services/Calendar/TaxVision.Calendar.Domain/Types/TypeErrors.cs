using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Types;

public static class TypeErrors
{
    public static readonly Error NotFound = new("Calendar.Type.NotFound", "The appointment type was not found.");

    public static readonly Error NameEmpty = new("Calendar.Type.NameEmpty", "The type needs a name.");

    public static readonly Error NameTooLong = new(
        "Calendar.Type.NameTooLong",
        $"The name cannot exceed {AppointmentType.MaxNameLength} characters."
    );

    public static readonly Error DurationOutOfRange = new(
        "Calendar.Type.DurationOutOfRange",
        $"The default duration must be between 5 minutes and {AppointmentType.MaxDurationHours} hours."
    );

    public static readonly Error ColorInvalid = new(
        "Calendar.Type.ColorInvalid",
        "The color must be a hex value like #1A2B3C."
    );

    public static readonly Error DailyCapOutOfRange = new(
        "Calendar.Type.DailyCapOutOfRange",
        "The daily cap must be a positive number, or unset."
    );
}
