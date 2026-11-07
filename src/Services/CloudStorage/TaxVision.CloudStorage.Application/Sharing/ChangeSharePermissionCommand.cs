using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>
/// Fase C4 (completitud) — cambia el Permission de un link ya creado. Reusa
/// exactamente la misma validacion de politica que la creacion (§20.4 del plan):
/// Upload/EditMetadata solo con cloudstorage.share.manage, y nunca junto con
/// Visibility.Public.
/// </summary>
public sealed record ChangeSharePermissionCommand(
    Guid TenantId,
    Guid ActorId,
    bool ActorHasManagePermission,
    Guid ShareLinkId,
    SharePermission NewPermission,
    RequestAuditContext Audit
);

public static class ChangeSharePermissionHandler
{
    public static async Task<Result<ShareLinkResponse>> Handle(
        ChangeSharePermissionCommand command,
        IShareLinkRepository shares,
        IStorageLimitRepository limits,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var link = await shares.GetAsync(command.TenantId, command.ShareLinkId, ct);
        if (link is null)
            return Result.Failure<ShareLinkResponse>(ShareErrors.NotFound);

        var policyResult = ShareLinkCreationCore.ValidatePolicy(
            command.ActorHasManagePermission,
            command.NewPermission,
            link.Visibility,
            recipientUserIds: [],
            recipientCustomerIds: [],
            recipientEmails: [],
            password: null,
            expiresAtUtc: null,
            maxAccessCount: null,
            clock.UtcNow,
            enforceLinkGuardrails: false,
            await limits.GetAsync(command.TenantId, ct)
        );
        if (policyResult.IsFailure)
            return Result.Failure<ShareLinkResponse>(policyResult.Error);

        var oldPermission = link.ChangePermission(command.NewPermission);
        if (oldPermission == command.NewPermission)
        {
            // No-op: no hace falta auditar ni publicar un evento por un cambio que no cambia nada.
            return Result.Success(ShareLinkResponseMapper.Map(link, clock.UtcNow));
        }

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                link.ResourceId,
                command.ActorId,
                "share.permission-changed",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                $"oldPermission={oldPermission};newPermission={command.NewPermission}",
                clock.UtcNow
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new ShareLinkPermissionChangedIntegrationEvent
            {
                TenantId = command.TenantId,
                ShareLinkId = link.Id,
                ResourceId = link.ResourceId,
                OldPermission = oldPermission.ToString(),
                NewPermission = command.NewPermission.ToString(),
                CorrelationId = command.Audit.CorrelationId,
            }
        );
        return Result.Success(ShareLinkResponseMapper.Map(link, clock.UtcNow));
    }
}
