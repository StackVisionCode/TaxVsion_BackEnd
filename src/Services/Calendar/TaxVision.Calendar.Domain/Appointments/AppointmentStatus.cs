namespace TaxVision.Calendar.Domain.Appointments;

public enum AppointmentStatus
{
    Confirmed = 1,
    Tentative = 2,
    Cancelled = 3,

    /// <summary>El cliente la pidió en el portal y la oficina todavía no la aprobó. Aparta el horario.</summary>
    Requested = 4,
}
