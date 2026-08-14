using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Appointments;

public static class AppointmentErrors
{
    public static readonly Error NotFound = new("Calendar.Appointment.NotFound", "The appointment was not found.");

    public static readonly Error TitleEmpty = new("Calendar.Appointment.TitleEmpty", "The title is required.");

    public static readonly Error TitleTooLong = new(
        "Calendar.Appointment.TitleTooLong",
        $"The title cannot exceed {ValueObjects.AppointmentTitle.MaxLength} characters."
    );

    public static readonly Error LocationEmpty = new(
        "Calendar.Appointment.LocationEmpty",
        "The location cannot be blank. Leave it unset instead."
    );

    public static readonly Error LocationTooLong = new(
        "Calendar.Appointment.LocationTooLong",
        $"The location cannot exceed {ValueObjects.Location.MaxLength} characters."
    );

    public static readonly Error OrganizerRequired = new(
        "Calendar.Appointment.OrganizerRequired",
        "Every appointment needs an organizer."
    );

    public static readonly Error TypeRequired = new(
        "Calendar.Appointment.TypeRequired",
        "Every appointment needs an appointment type."
    );

    /// <summary>Sin esta regla dos personas mueven la misma cita a la vez.</summary>
    public static readonly Error NotTheOrganizer = new(
        "Calendar.Appointment.NotTheOrganizer",
        "Only the organizer can move or cancel this appointment."
    );

    public static readonly Error AlreadyCancelled = new(
        "Calendar.Appointment.AlreadyCancelled",
        "The appointment is already cancelled."
    );

    public static readonly Error CancelledIsFinal = new(
        "Calendar.Appointment.CancelledIsFinal",
        "A cancelled appointment cannot be changed. Create a new one."
    );

    public static readonly Error AttendeeNameEmpty = new(
        "Calendar.Appointment.AttendeeNameEmpty",
        "The attendee needs a display name."
    );

    public static readonly Error AttendeeNameTooLong = new(
        "Calendar.Appointment.AttendeeNameTooLong",
        $"The attendee name cannot exceed {ValueObjects.AttendeeSnapshot.MaxNameLength} characters."
    );

    public static readonly Error AttendeeEmailInvalid = new(
        "Calendar.Appointment.AttendeeEmailInvalid",
        "The attendee email is not a valid address."
    );

    public static readonly Error AttendeeNotFound = new(
        "Calendar.Appointment.AttendeeNotFound",
        "That attendee is not on this appointment."
    );

    public static readonly Error AttendeeAlreadyAdded = new(
        "Calendar.Appointment.AttendeeAlreadyAdded",
        "That attendee is already on this appointment."
    );

    public static readonly Error TooManyAttendees = new(
        "Calendar.Appointment.TooManyAttendees",
        $"An appointment cannot have more than {Appointment.MaxAttendees} attendees."
    );

    public static readonly Error OrganizerCannotBeRemoved = new(
        "Calendar.Appointment.OrganizerCannotBeRemoved",
        "The organizer cannot be removed from their own appointment."
    );
}
