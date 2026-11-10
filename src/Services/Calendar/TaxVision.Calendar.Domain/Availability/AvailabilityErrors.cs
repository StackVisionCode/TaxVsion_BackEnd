using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Availability;

public static class AvailabilityErrors
{
    public static readonly Error NotFound = new("Calendar.Availability.NotFound", "The rule was not found.");

    public static readonly Error UserRequired = new(
        "Calendar.Availability.UserRequired",
        "An availability rule belongs to a person."
    );

    public static readonly Error EndBeforeStart = new(
        "Calendar.Availability.EndBeforeStart",
        "The end of the window must come after its start."
    );

    public static readonly Error NoDays = new(
        "Calendar.Availability.NoDays",
        "The rule must apply to at least one day of the week."
    );

    public static readonly Error ReasonTooLong = new(
        "Calendar.Availability.ReasonTooLong",
        $"The reason cannot exceed {BlockedTime.MaxReasonLength} characters."
    );

    /// <summary>Tocar la agenda de otra persona sin <c>calendar.availability.manage</c>; la web responde 403.</summary>
    public static readonly Error Forbidden = new(
        "Calendar.Availability.Forbidden",
        "You can only change your own availability."
    );

    public static readonly Error TooManyRules = new(
        "Calendar.Availability.TooManyRules",
        "A weekly schedule can have up to 21 windows."
    );

    public static readonly Error BlockNotFound = new(
        "Calendar.Availability.BlockNotFound",
        "The blocked time was not found."
    );
}
