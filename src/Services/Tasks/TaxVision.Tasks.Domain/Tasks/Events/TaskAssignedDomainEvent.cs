using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>Cambió el responsable. El anterior viaja porque a él también hay que avisarle.</summary>
public sealed record TaskAssignedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid AssigneeUserId,
    Guid? PreviousAssigneeUserId,
    Guid AssignedByUserId,
    DateTime AssignedAtUtc
) : IDomainEvent;
