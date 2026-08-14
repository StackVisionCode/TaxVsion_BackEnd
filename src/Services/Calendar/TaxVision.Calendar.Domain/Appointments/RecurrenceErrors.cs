using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>Errores de la recurrencia, sus excepciones y la expansion.</summary>
public static class RecurrenceErrors
{
    public static readonly Error RuleEmpty = new("Calendar.Recurrence.RuleEmpty", "The recurrence rule is required.");

    public static readonly Error RuleTooLong = new(
        "Calendar.Recurrence.RuleTooLong",
        $"The recurrence rule cannot exceed {ValueObjects.RecurrenceRule.MaxLength} characters."
    );

    public static readonly Error RuleInvalid = new(
        "Calendar.Recurrence.RuleInvalid",
        "The recurrence rule is not a valid RFC 5545 RRULE."
    );

    public static readonly Error UntilNotUtc = new(
        "Calendar.Recurrence.UntilNotUtc",
        "RFC 5545 requires UNTIL to be expressed in UTC."
    );

    public static readonly Error NotRecurring = new(
        "Calendar.Exception.NotRecurring",
        "Only a recurring appointment can have occurrence exceptions."
    );

    /// <summary>La que se olvida, y sin ella la tabla se llena de basura que nadie ve.</summary>
    public static readonly Error NotAnOccurrence = new(
        "Calendar.Exception.NotAnOccurrence",
        "That instant is not an occurrence this series produces."
    );

    public static readonly Error DuplicateException = new(
        "Calendar.Exception.Duplicate",
        "That occurrence already has an exception."
    );

    public static readonly Error CancelledWithOverrides = new(
        "Calendar.Exception.CancelledWithOverrides",
        "A cancelled occurrence cannot carry overridden values."
    );

    public static readonly Error EmptyOverride = new(
        "Calendar.Exception.EmptyOverride",
        "An overridden occurrence must change at least one value."
    );

    public static readonly Error ExceptionNotFound = new(
        "Calendar.Exception.NotFound",
        "That occurrence has no exception."
    );

    public static readonly Error RangeTooLarge = new(
        "Calendar.Occurrences.RangeTooLarge",
        $"The requested range would produce more than {Scheduling.OccurrenceExpander.MaxOccurrencesPerQuery} "
            + "occurrences. Narrow it down."
    );

    public static readonly Error RangeInverted = new(
        "Calendar.Occurrences.RangeInverted",
        "The end of the range must come after its start."
    );

    /// <summary>Partir sobre la primera ocurrencia dejaria la serie vieja vacia.</summary>
    public static readonly Error SplitOnFirstOccurrence = new(
        "Calendar.Appointment.SplitOnFirstOccurrence",
        "Splitting on the first occurrence would leave the original series empty. Edit the whole series instead."
    );

    public static readonly Error NotASeries = new(
        "Calendar.Appointment.NotASeries",
        "That operation only applies to a recurring appointment."
    );
}
