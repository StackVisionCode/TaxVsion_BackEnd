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
/// de alguien que ya no está no debe seguir vivo — y lo saca de los destinatarios de los links activos
/// de otros (si un link de destinatarios queda sin nadie, se revoca). NO toca archivos ni carpetas (los
/// personales quedan con su dueño; nada de clientes/tenant/módulos/sys/legal-hold ni el ObjectKey se
/// toca). Idempotente: al reprocesar, lo ya resuelto no reentra. Guarda antes de publicar.
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

        var created = await shares.ListActiveByCreatorAsync(msg.TenantId, msg.UserId, ct);
        var asRecipient = await shares.ListActiveWithUserRecipientAsync(msg.TenantId, msg.UserId, ct);
        if (created.Count == 0 && asRecipient.Count == 0)
            return;

        var now = clock.UtcNow;
        var actorId = msg.OffboardedByUserId ?? msg.UserId;
        var revoked = new List<ShareLink>();
        foreach (var link in created)
        {
            if (link.Revoke(now).IsFailure)
                continue;
            revoked.Add(link);
            audit.Add(Audit(msg, link, actorId, "share.revoke", "owner offboarded", correlation, now));
        }

        // Los suyos ya quedaron revocados arriba.
        var createdIds = created.Select(link => link.Id).ToHashSet();
        var removedFrom = 0;
        foreach (var link in asRecipient.Where(link => !createdIds.Contains(link.Id)))
        {
            if (!link.RemoveUserRecipient(msg.UserId))
                continue;
            removedFrom++;
            audit.Add(Audit(msg, link, actorId, "share.recipient.remove", "recipient offboarded", correlation, now));

            if (link.RequiresRecipients && !link.HasAnyRecipient && link.Revoke(now).IsSuccess)
            {
                revoked.Add(link);
                audit.Add(Audit(msg, link, actorId, "share.revoke", "last recipient offboarded", correlation, now));
            }
        }

        if (revoked.Count == 0 && removedFrom == 0)
            return;

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var link in revoked)
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

        logger.LogInformation(
            "Offboarded user {UserId}: revoked {Revoked} share link(s) and removed them from {Removed} as recipient in tenant {TenantId}.",
            msg.UserId,
            revoked.Count,
            removedFrom,
            msg.TenantId
        );
    }

    private static StorageAccessLog Audit(
        UserOffboardedIntegrationEvent msg,
        ShareLink link,
        Guid actorId,
        string action,
        string details,
        ICorrelationContext correlation,
        DateTime now
    ) =>
        StorageAccessLog.Create(
            msg.TenantId,
            link.ResourceId,
            actorId,
            action,
            "success",
            null,
            null,
            correlation.CorrelationId,
            details,
            now
        );
}
