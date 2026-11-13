using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>Errores de la configuración de una página de reservas pública.</summary>
public static class BookingPageErrors
{
    public static readonly Error NotFound = new("Calendar.BookingPage.NotFound", "The booking page was not found.");

    public static readonly Error TenantNotAllowed = new(
        "Calendar.BookingPage.TenantNotAllowed",
        "Public booking pages belong to the platform."
    );

    public static readonly Error SlugTaken = new(
        "Calendar.BookingPage.SlugTaken",
        "Another page already uses that name."
    );

    public static readonly Error HostNotFound = new(
        "Calendar.BookingPage.HostNotFound",
        "We couldn't find that user in the platform. Try again in a moment."
    );

    public static readonly Error HostInactive = new(
        "Calendar.BookingPage.HostInactive",
        "That user is no longer active."
    );

    public static readonly Error SlugInvalid = new(
        "Calendar.BookingPage.SlugInvalid",
        "Use 3 to 40 lowercase letters, numbers or hyphens."
    );

    public static readonly Error TextRequired = new(
        "Calendar.BookingPage.TextRequired",
        "The English text is required."
    );

    public static readonly Error TextTooLong = new("Calendar.BookingPage.TextTooLong", "The text is too long.");

    public static readonly Error TypeNotUsable = new(
        "Calendar.BookingPage.TypeNotUsable",
        "Use an active, in-person appointment type that blocks conflicts."
    );

    public static readonly Error RulesOutOfRange = new(
        "Calendar.BookingPage.RulesOutOfRange",
        "Notice up to 14 days, window 1 to 365 days, buffer up to 240 minutes, a slot step of 5, 10, 15, 20, 30 or 60 minutes, a reminder of 1 to 168 hours and up to 3 alarms of 1 minute to 7 days."
    );

    public static readonly Error TooManyHosts = new(
        "Calendar.BookingPage.TooManyHosts",
        "A page can have up to 20 hosts."
    );

    public static readonly Error DuplicatedHost = new(
        "Calendar.BookingPage.DuplicatedHost",
        "Each host can appear only once."
    );

    public static readonly Error HostNameInvalid = new(
        "Calendar.BookingPage.HostNameInvalid",
        "Each host needs a display name of up to 200 characters."
    );

    public static readonly Error EmailInvalid = new(
        "Calendar.BookingPage.EmailInvalid",
        "Enter a valid email address."
    );

    public static readonly Error MeetingUrlInvalid = new(
        "Calendar.BookingPage.MeetingUrlInvalid",
        "Each host needs an https meeting link of up to 300 characters."
    );

    public static readonly Error NoActiveHost = new(
        "Calendar.BookingPage.NoActiveHost",
        "An active page needs at least one active host."
    );

    public static readonly Error TooManyOptions = new(
        "Calendar.BookingPage.TooManyOptions",
        "A page can have up to 30 answers for \"How did you hear about us?\"."
    );

    public static readonly Error DuplicatedOption = new(
        "Calendar.BookingPage.DuplicatedOption",
        "Each answer code can appear only once."
    );

    public static readonly Error OptionCodeInvalid = new(
        "Calendar.BookingPage.OptionCodeInvalid",
        "Answer codes use 2 to 40 lowercase letters, numbers or underscores, starting with a letter."
    );

    public static readonly Error OptionLabelInvalid = new(
        "Calendar.BookingPage.OptionLabelInvalid",
        "Each answer needs an English label of up to 100 characters."
    );

    public static readonly Error NoHeardAboutOption = new(
        "Calendar.BookingPage.NoHeardAboutOption",
        "An active page needs at least one active answer for \"How did you hear about us?\"."
    );

    public static readonly Error NoConsent = new(
        "Calendar.BookingPage.NoConsent",
        "An active page needs the privacy notice and the marketing consent text."
    );

    public static readonly Error ConsentVersionInvalid = new(
        "Calendar.BookingPage.ConsentVersionInvalid",
        "The consent version is required and up to 40 characters."
    );
}
