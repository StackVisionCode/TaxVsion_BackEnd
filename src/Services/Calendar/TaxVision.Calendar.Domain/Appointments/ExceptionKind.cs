namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>
/// Los dos mecanismos del RFC 5545, y hacen falta los dos: <c>EXDATE</c> quita una ocurrencia,
/// <c>RECURRENCE-ID</c> la cambia.
/// </summary>
public enum ExceptionKind
{
    Cancelled = 1,
    Overridden = 2,
}
