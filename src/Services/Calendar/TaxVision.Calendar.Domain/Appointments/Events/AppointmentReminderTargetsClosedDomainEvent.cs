using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>Estos objetivos de aviso ya no valen. Viajan aquí porque después del cambio la cita ya no los produce.</summary>
public sealed record AppointmentReminderTargetsClosedDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    IReadOnlyList<Guid> TargetIds,
    string? Reason,
    DateTime ClosedAtUtc
) : IDomainEvent;
