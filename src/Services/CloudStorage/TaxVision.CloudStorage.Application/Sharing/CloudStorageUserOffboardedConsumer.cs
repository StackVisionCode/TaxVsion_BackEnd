using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Sharing;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: revoca los share links ACTIVOS que él creó — un link público
/// de alguien que ya no está no debe seguir vivo. NO toca archivos ni carpetas (los personales quedan
/// con su dueño; nada de clientes/tenant/módulos/sys/legal-hold ni el ObjectKey se toca). Idempotente:
/// al reprocesar, los links ya revocados no reentran (Status == Active).
/// </summary>
public static class CloudStorageUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        IShareLinkRepository shares,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<ShareLink> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        var links = await shares.ListActiveByCreatorAsync(msg.TenantId, msg.UserId, ct);
        if (links.Count == 0)
            return;

        var now = clock.UtcNow;
        var actorId = msg.OffboardedByUserId ?? msg.UserId;
        foreach (var link in links)
        {
            if (link.Revoke(now).IsFailure)
                continue;

            audit.Add(
                StorageAccessLog.Create(
                    msg.TenantId,
                    link.ResourceId,
                    actorId,
                    "share.revoke",
                    "success",
                    null,
                    null,
                    correlation.CorrelationId,
                    "owner offboarded",
                    now
                )
            );
            await bus.PublishAsync(
                new ShareLinkRevokedIntegrationEvent
                {
                    TenantId = msg.TenantId,
                    ShareLinkId = link.Id,
                    ResourceId = link.ResourceId,
                    ResourceType = link.ResourceType.ToString(),
                    CorrelationId = correlation.CorrelationId,
                }
            );
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Offboarded user {UserId}: revoked {Count} share link(s) in tenant {TenantId}.",
            msg.UserId,
            links.Count,
            msg.TenantId
        );
    }
}
