using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>La serie se partio en dos por un «esta y las siguientes». Quien tenga recordatorios de la mitad nueva tiene que repuntarlos.</summary>
public sealed record AppointmentSeriesSplitDomainEvent(
    Guid OriginalAppointmentId,
    Guid FollowerAppointmentId,
    Guid TenantId,
    DateTime CutOriginalStartUtc,
    Guid SplitByUserId,
    DateTime SplitAtUtc
) : IDomainEvent;
