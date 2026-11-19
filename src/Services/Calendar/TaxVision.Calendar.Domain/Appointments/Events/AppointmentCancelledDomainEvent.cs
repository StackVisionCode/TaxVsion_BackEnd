using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>La cita se cancelo. No se borra: el historial de por que no se atendio a alguien importa.</summary>
public sealed record AppointmentCancelledDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    Guid CancelledByUserId,
    string? Reason,
    DateTime CancelledAtUtc,
    EditScope? Scope,
    DateTime? OriginalStartUtc,
    // Empleado que la canceló; null si fue el cliente o un proceso.
    Guid? ChangedByUserId = null
) : IDomainEvent;
