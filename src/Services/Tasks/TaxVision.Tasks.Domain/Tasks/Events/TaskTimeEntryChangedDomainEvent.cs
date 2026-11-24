using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>Se corrigió o se borró un tramo de tiempo. <paramref name="Hours"/> nulo = borrado.</summary>
public sealed record TaskTimeEntryChangedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid EntryId,
    Guid EntryUserId,
    decimal PreviousHours,
    decimal? Hours,
    Guid ByUserId,
    DateTime AtUtc
) : IDomainEvent;
