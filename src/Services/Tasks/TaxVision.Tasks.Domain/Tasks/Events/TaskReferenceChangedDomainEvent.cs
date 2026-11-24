using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>Cambió el cliente o el año fiscal de la tarea. Para el historial.</summary>
public sealed record TaskReferenceChangedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid? PreviousCustomerId,
    Guid? CustomerId,
    int? PreviousTaxYear,
    int? TaxYear,
    Guid ChangedByUserId,
    DateTime ChangedAtUtc
) : IDomainEvent;
