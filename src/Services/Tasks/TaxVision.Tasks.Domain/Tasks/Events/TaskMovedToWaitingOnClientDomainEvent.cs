using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>
/// Se le pidió algo al cliente. <paramref name="ExpectedItems"/> viaja en el evento porque termina
/// dentro del correo que recibe.
/// </summary>
public sealed record TaskMovedToWaitingOnClientDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid? CustomerId,
    int? TaxYear,
    string ExpectedItems,
    DateTime? ClientDueAtUtc,
    Guid RequestedByUserId,
    DateTime RequestedAtUtc
) : IDomainEvent;
