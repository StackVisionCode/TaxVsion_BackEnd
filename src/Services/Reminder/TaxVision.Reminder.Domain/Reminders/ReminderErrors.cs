using BuildingBlocks.Results;

namespace TaxVision.Reminder.Domain.Reminders;

/// <summary>
/// Cada código se mapea en <c>ErrorHttpMapping</c> (Fase 6). Sin ese mapeo, un error de dominio sale
/// como 500 en vez de 400/404/409 — es el fallo que ya hubo que corregir en Scribe y Postmaster.
/// </summary>
public static class ReminderErrors
{
    /// <summary>
    /// También es la respuesta para un recordatorio ajeno. No existe un <c>NotOwner</c>: devolver
    /// 403 confirmaría que ese id existe en el tenant, y un recordatorio es estrictamente privado.
    /// El <c>UserId</c> viaja dentro del predicado SQL, así que «no es tuyo» y «no existe» son
    /// indistinguibles por construcción.
    /// </summary>
    public static readonly Error NotFound = new("Reminder.NotFound", "Reminder was not found.");

    public static readonly Error SnoozeLimitReached = new(
        "Reminder.SnoozeLimitReached",
        $"A reminder cannot be snoozed more than {Reminder.MaxSnoozeCount} times."
    );

    public static readonly Error SnoozeDurationInvalid = new(
        "Reminder.SnoozeDurationInvalid",
        "Snooze duration must be positive."
    );

    public static readonly Error DuplicateRequest = new(
        "Reminder.DuplicateRequest",
        "A reminder with the same request key already exists for this tenant."
    );

    public static readonly Error CancellationReasonRequired = new(
        "Reminder.CancellationReasonRequired",
        "A cancellation reason is required."
    );

    public static readonly Error OwnerRequired = new(
        "Reminder.OwnerRequired",
        "Both tenant and user are required to create a reminder."
    );

    public static Error InvalidTransition(ReminderStatus from, string operation) =>
        new("Reminder.InvalidTransition", $"Cannot {operation} a reminder in status {from}.");

    public static class Schedule
    {
        public static readonly Error InThePast = new(
            "Reminder.Schedule.InThePast",
            "The reminder would fire in the past."
        );

        public static readonly Error LeadOutOfRange = new(
            "Reminder.Schedule.LeadOutOfRange",
            "Lead minutes must be between 0 and 525600 (one year)."
        );

        public static readonly Error NotUtc = new(
            "Reminder.Schedule.NotUtc",
            "Schedule timestamps must be expressed in UTC."
        );

        public static readonly Error NotAnchored = new(
            "Reminder.Schedule.NotAnchored",
            "An absolute schedule cannot be recalculated against a new anchor."
        );

        public static readonly Error TooFarInFuture = new(
            "Reminder.Schedule.TooFarInFuture",
            "The reminder would fire more than 5 years from now."
        );
    }

    public static class Target
    {
        public static readonly Error UnexpectedTarget = new(
            "Reminder.Target.UnexpectedTarget",
            "A General reminder cannot reference a target."
        );

        public static readonly Error TargetRequired = new(
            "Reminder.Target.TargetRequired",
            "A non-General reminder requires a target id."
        );
    }

    public static class Subject
    {
        public static readonly Error TitleRequired = new("Reminder.Subject.TitleRequired", "A title is required.");

        public static readonly Error TitleTooLong = new(
            "Reminder.Subject.TitleTooLong",
            "The title exceeds the maximum length."
        );

        public static readonly Error BodyTooLong = new(
            "Reminder.Subject.BodyTooLong",
            "The body exceeds the maximum length."
        );
    }

    public static class TimeZone
    {
        public static readonly Error Invalid = new(
            "Reminder.TimeZone.Invalid",
            "The time zone is not a recognized IANA identifier."
        );
    }

    public static class RequestKey
    {
        public static readonly Error Required = new(
            "Reminder.RequestKey.Required",
            "A request key is required for idempotency."
        );

        public static readonly Error TooLong = new(
            "Reminder.RequestKey.TooLong",
            "The request key exceeds the maximum length."
        );
    }
}
