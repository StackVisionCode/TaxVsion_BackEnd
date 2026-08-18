using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>
/// El último bloqueador cayó y <c>OpenBlockerCount</c> llegó a 0. Sin este evento el desbloqueo es
/// invisible hasta que alguien refresca la lista.
/// </summary>
public sealed record TaskUnblockedDomainEvent(Guid TaskId, Guid TenantId, Guid? AssigneeUserId, DateTime UnblockedAtUtc)
    : IDomainEvent;
