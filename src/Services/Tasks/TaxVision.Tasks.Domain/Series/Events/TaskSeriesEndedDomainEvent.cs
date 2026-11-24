using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Series.Events;

/// <summary>La serie terminó sola (límite, fecha de fin o regla agotada): se avisa a quien la gestiona.</summary>
public sealed record TaskSeriesEndedDomainEvent(Guid SeriesId, Guid TenantId, Guid OwnerUserId, string Title)
    : IDomainEvent;
