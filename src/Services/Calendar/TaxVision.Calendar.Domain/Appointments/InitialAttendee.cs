using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>Un asistente que viene con el alta de la cita. El organizador llega ya aceptado.</summary>
public sealed record InitialAttendee(
    AttendeeKind Kind,
    Guid? UserId,
    Guid? CustomerId,
    AttendeeSnapshot Snapshot,
    bool IsRequired,
    bool Accepted = false
);
