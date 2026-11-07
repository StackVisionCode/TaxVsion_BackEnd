using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>Fase C3 — revoca un link de compartir. Irreversible: no existe "des-revocar".</summary>
public sealed record RevokeShareLinkCommand(Guid TenantId, Guid ActorId, Guid ShareLinkId, RequestAuditContext Audit);

public static class RevokeShareLinkHandler
{
    public static async Task<Result> Handle(
        RevokeShareLinkCommand command,
        IShareLinkRepository shares,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var link = await shares.GetAsync(command.TenantId, command.ShareLinkId, ct);
        if (link is null)
            return Result.Failure(ShareErrors.NotFound);

        var revoked = link.Revoke(clock.UtcNow);
        if (revoked.IsFailure)
            return Result.Failure(revoked.Error);

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                link.ResourceId,
                command.ActorId,
                "share.revoke",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                null,
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new ShareLinkRevokedIntegrationEvent
            {
                TenantId = command.TenantId,
                ShareLinkId = link.Id,
                ResourceId = link.ResourceId,
                ResourceType = link.ResourceType.ToString(),
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        return Result.Success();
    }
}
