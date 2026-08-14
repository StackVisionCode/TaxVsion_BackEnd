namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>De donde sale el asistente. Los tres se guardan igual: como snapshot.</summary>
public enum AttendeeKind
{
    InternalUser = 1,
    Customer = 2,
    External = 3,
}
