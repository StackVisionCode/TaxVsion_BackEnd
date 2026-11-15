using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>Errores de la reserva pública. Los mensajes los ve el visitante: claros y sin detalles internos.</summary>
public static class PublicBookingErrors
{
    public static readonly Error NameRequired = new("Calendar.PublicBooking.NameRequired", "Enter your full name.");

    public static readonly Error NameTooLong = new("Calendar.PublicBooking.NameTooLong", "Your name is too long.");

    public static readonly Error EmailInvalid = new("Calendar.PublicBooking.EmailInvalid", "Enter a valid work email.");

    public static readonly Error CompanyRequired = new(
        "Calendar.PublicBooking.CompanyRequired",
        "Enter your company name."
    );

    public static readonly Error CompanyTooLong = new(
        "Calendar.PublicBooking.CompanyTooLong",
        "The company name is too long."
    );

    public static readonly Error PhoneInvalid = new(
        "Calendar.PublicBooking.PhoneInvalid",
        "Enter a valid phone number."
    );

    public static readonly Error TextInvalid = new(
        "Calendar.PublicBooking.TextInvalid",
        "Please remove line breaks or special characters."
    );

    public static readonly Error InterestRequired = new(
        "Calendar.PublicBooking.InterestRequired",
        "Tell us what you'd like to see."
    );

    public static readonly Error InterestTooLong = new(
        "Calendar.PublicBooking.InterestTooLong",
        "Please keep it under 500 characters."
    );

    public static readonly Error HeardAboutRequired = new(
        "Calendar.PublicBooking.HeardAboutRequired",
        "Tell us how you heard about us."
    );

    public static readonly Error HeardAboutDetailNotAllowed = new(
        "Calendar.PublicBooking.HeardAboutDetailNotAllowed",
        "This answer doesn't take extra details."
    );

    public static readonly Error HeardAboutDetailTooLong = new(
        "Calendar.PublicBooking.HeardAboutDetailTooLong",
        "Please keep it under 100 characters."
    );

    public static readonly Error AttributionInvalid = new(
        "Calendar.PublicBooking.AttributionInvalid",
        "Invalid attribution data."
    );

    public static readonly Error SlotInPast = new(
        "Calendar.PublicBooking.SlotInPast",
        "That time has already passed. Pick another one."
    );

    public static readonly Error IdempotencyKeyInvalid = new(
        "Calendar.PublicBooking.IdempotencyKeyInvalid",
        "The Idempotency-Key header is required and up to 100 characters."
    );

    public static readonly Error RequestInvalid = new(
        "Calendar.PublicBooking.RequestInvalid",
        "The booking request is not valid."
    );

    public static readonly Error NotPending = new(
        "Calendar.PublicBooking.NotPending",
        "This request is no longer waiting for confirmation."
    );

    public static readonly Error HoldExpired = new(
        "Calendar.PublicBooking.HoldExpired",
        "Your hold expired. Please pick a time again."
    );

    public static readonly Error InvalidCode = new(
        "Calendar.PublicBooking.InvalidCode",
        "That code isn't right. Check your email and try again."
    );

    public static readonly Error TooManyAttempts = new(
        "Calendar.PublicBooking.TooManyAttempts",
        "Too many wrong codes. Please pick a time again."
    );

    public static readonly Error TooManyCodes = new(
        "Calendar.PublicBooking.TooManyCodes",
        "We already sent several codes. Please check your email or pick a time again."
    );

    public static readonly Error NotVerified = new("Calendar.PublicBooking.NotVerified", "Confirm your email first.");

    public static readonly Error NotConfirmed = new(
        "Calendar.PublicBooking.NotConfirmed",
        "This demo is not active anymore."
    );

    public static readonly Error AlreadyHappened = new(
        "Calendar.PublicBooking.AlreadyHappened",
        "This demo already took place."
    );

    public static readonly Error NotFinishedYet = new(
        "Calendar.PublicBooking.NotFinishedYet",
        "The demo hasn't finished yet."
    );

    public static readonly Error ReminderNotDue = new(
        "Calendar.PublicBooking.ReminderNotDue",
        "It's not time for the reminder yet."
    );

    public static readonly Error ReminderNotNeeded = new(
        "Calendar.PublicBooking.ReminderNotNeeded",
        "The demo was booked or moved inside the reminder window."
    );

    public static readonly Error ReminderAlreadySent = new(
        "Calendar.PublicBooking.ReminderAlreadySent",
        "The reminder was already sent."
    );

    public static readonly Error RepeatNoticeTooSoon = new(
        "Calendar.PublicBooking.RepeatNoticeTooSoon",
        "The visitor was already told about the booked demo within the last hour."
    );

    public static readonly Error NotClosed = new(
        "Calendar.PublicBooking.NotClosed",
        "Only closed bookings can be anonymized."
    );

    public static readonly Error NotFound = new(
        "Calendar.PublicBooking.NotFound",
        "This page or link is not available."
    );

    public static readonly Error SlotTaken = new(
        "Calendar.PublicBooking.SlotTaken",
        "That time was just taken. Please pick another one."
    );

    public static readonly Error FieldRequired = new(
        "Calendar.PublicBooking.FieldRequired",
        "Please fill in the required fields."
    );

    public static readonly Error ConsentOutdated = new(
        "Calendar.PublicBooking.ConsentOutdated",
        "Our terms were just updated. Please reload the page and try again."
    );

    public static readonly Error RangeInvalid = new(
        "Calendar.PublicBooking.RangeInvalid",
        "Choose a range of up to 14 days."
    );

    public static readonly Error ReasonTooLong = new(
        "Calendar.PublicBooking.ReasonTooLong",
        "Please keep the reason under 500 characters."
    );
}
