namespace TaxVision.Correspondence.Application.Compose;

/// <param name="VisibleAccountIds">Ver <see cref="SendingAccountGuard"/>.</param>
public sealed record StartReplyCommand(
    Guid TenantId,
    Guid IncomingEmailId,
    Guid AccountId,
    Guid ActorId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null
);
