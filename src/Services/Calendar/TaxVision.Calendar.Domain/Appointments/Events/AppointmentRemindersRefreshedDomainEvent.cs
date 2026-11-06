using BuildingBlocks.Domain;

namespace TaxVision.Calendar.Domain.Appointments.Events;

/// <summary>
/// Hay que pedir de nuevo los avisos de la cita. `KeptTargetIds` siguen vivos: no se piden otra vez, y si
/// `LeadChanged` reciben los minutos nuevos sobre el mismo objetivo.
/// </summary>
public sealed record AppointmentRemindersRefreshedDomainEvent(
    Guid AppointmentId,
    Guid TenantId,
    IReadOnlyList<Guid> KeptTargetIds,
    bool LeadChanged,
    DateTime RefreshedAtUtc
) : IDomainEvent;
