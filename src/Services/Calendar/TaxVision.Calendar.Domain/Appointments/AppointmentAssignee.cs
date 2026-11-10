using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>A quién se le da la cita al aprobar una solicitud: un empleado, con su nombre y correo.</summary>
public sealed record AppointmentAssignee(Guid UserId, AttendeeSnapshot Snapshot);
