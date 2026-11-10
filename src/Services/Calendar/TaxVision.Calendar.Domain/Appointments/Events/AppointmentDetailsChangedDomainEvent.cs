using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>
/// Cambiaron título, descripción o lugar. El título viejo viaja porque el aviso dice "X ahora se llama Y":
/// sin él nadie reconoce cuál cita cambió.
/// </summary>
public sealed record AppointmentDetailsChangedDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    string PreviousTitle,
    string[] ChangedFields,
    Guid ChangedByUserId,
    DateTime ChangedAtUtc
) : IDomainEvent;
