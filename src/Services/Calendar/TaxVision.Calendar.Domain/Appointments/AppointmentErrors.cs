using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Appointments;

public static class AppointmentErrors
{
    public static readonly Error NotFound = new("Calendar.Appointment.NotFound", "The appointment was not found.");

    public static readonly Error RangeInvalid = new(
        "Calendar.Appointment.RangeInvalid",
        "The range must start before it ends and span at most 62 days."
    );

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

    public static readonly Error ReminderLeadTooLong = new(
        "Calendar.Appointment.ReminderLeadTooLong",
        $"The reminder cannot be set more than {Appointment.MaxReminderLeadMinutes} minutes before the appointment."
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

    public static readonly Error InvalidResponse = new(
        "Calendar.Appointment.InvalidResponse",
        "The response must be accepted, declined or tentative."
    );

    public static readonly Error AssigneeUnavailable = new(
        "Calendar.Appointment.AssigneeUnavailable",
        "That team member can't take appointments right now."
    );

    public static readonly Error NotARequest = new(
        "Calendar.Appointment.NotARequest",
        "This appointment isn't waiting for approval."
    );

    public static readonly Error NotTheCustomer = new(
        "Calendar.Appointment.NotTheCustomer",
        "Only the appointment's client can change it from the portal."
    );

    public static readonly Error SeriesChangedByOffice = new(
        "Calendar.Appointment.SeriesChangedByOffice",
        "A recurring appointment is changed by the office."
    );

    public static readonly Error DescriptionTooLong = new(
        "Calendar.Appointment.DescriptionTooLong",
        $"The description cannot be longer than {Appointment.MaxDescriptionLength} characters."
    );

    public static readonly Error RsvpClosed = new(
        "Calendar.Appointment.RsvpClosed",
        "This appointment already took place."
    );

    public static readonly Error AttendeeAlreadyAdded = new(
        "Calendar.Appointment.AttendeeAlreadyAdded",
        "That attendee is already on this appointment."
    );

    public static readonly Error TooManyAttendees = new(
        "Calendar.Appointment.TooManyAttendees",
        $"An appointment cannot have more than {Appointment.MaxAttendees} attendees."
    );

    /// <summary>Solapamiento que el tipo declara como error, no como aviso. La capa web lo mapea a 409.</summary>
    /// <summary>El tipo tiene tope diario y el organizador ya lo alcanzó ese día (su día local).</summary>
    public static readonly Error DailyCapReached = new(
        "Calendar.Appointment.DailyCapReached",
        "The daily limit for this appointment type is already reached that day."
    );

    /// <summary>Un invitado del equipo ya tiene otra cita a esa hora y el tipo no admite solapes.</summary>
    public static Error AttendeeConflict(string name) =>
        new(
            "Calendar.Appointment.AttendeeConflict",
            $"{name} already has an appointment at that time and this type does not allow overlaps."
        );

    public static readonly Error Conflict = new(
        "Calendar.Appointment.Conflict",
        "The appointment overlaps another one and this type does not allow it."
    );

    /// <summary>Otro la reprogramó mientras se respondía. Mismo código que un choque de versión: el CRM ya sabe qué decir.</summary>
    public static readonly Error ScheduleChanged = new(
        "Persistence.ConcurrencyConflict",
        "The appointment was rescheduled while you were responding. Check the new time and respond again."
    );

    public static readonly Error ScopeRequired = new(
        "Calendar.Appointment.ScopeRequired",
        "Editing an occurrence of a series requires an explicit scope."
    );

    public static readonly Error OrganizerCannotBeRemoved = new(
        "Calendar.Appointment.OrganizerCannotBeRemoved",
        "The organizer cannot be removed from their own appointment."
    );
}
