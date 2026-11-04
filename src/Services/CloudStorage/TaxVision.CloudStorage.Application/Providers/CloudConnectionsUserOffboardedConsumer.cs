using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Providers;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Providers;

/// <summary>
/// Al RETIRAR a un empleado: las conexiones de nube de la oficina ligadas a él (su cuenta o autorizadas
/// por él) NO se revocan — son de la oficina y cortarlas a ciegas frenaría las copias. Se marcan para
/// revisión y se avisa a quienes administran el almacenamiento. Idempotente (una ya marcada no se
/// re-marca ni re-avisa) y sin asumir orden: si la oficina reconectó después del retiro, no se toca.
/// Descarta además sus <c>state</c> OAuth pendientes: no puede terminar una conexión empezada antes.
/// </summary>
public static class CloudConnectionsUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        ITenantCloudConnectionRepository connections,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantCloudConnection> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        // Un intento de conexión que él dejó a medias no debe poder completarse.
        var discardedStates = await connections.DiscardOAuthStatesInitiatedByAsync(msg.TenantId, msg.UserId, ct);

        var tenantConnections = await connections.ListByTenantAsync(msg.TenantId, ct);
        var now = clock.UtcNow;
        var flagged = new List<(TenantCloudConnection Connection, string Reason)>();
        foreach (var connection in tenantConnections)
        {
            // Reconectada después del retiro por otro = ya la revisó alguien vigente; si la terminó él, no.
            if (connection.ConnectedAtUtc > msg.RemovedAtUtc && connection.ConnectedByUserId != msg.UserId)
                continue;
            if (connection.LinkedReasonFor(msg.UserId, msg.Email) is not { } reason)
                continue;
            if (connection.RequireReview(reason, now))
                flagged.Add((connection, reason));
        }

        if (flagged.Count == 0 && discardedStates == 0)
            return;

        // Guardado antes de publicar: un reintento no re-avisa lo que ya quedó marcado.
        await unitOfWork.SaveChangesAsync(ct);

        foreach (var (connection, reason) in flagged)
            await bus.PublishAsync(
                new CloudConnectionReviewRequiredIntegrationEvent
                {
                    TenantId = msg.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    ConnectionId = connection.Id,
                    Provider = connection.Provider,
                    AccountEmail = connection.AccountEmail,
                    Reason = reason,
                    OffboardedUserId = msg.UserId,
                    AudiencePermission = CloudStoragePermissions.SettingsManage,
                    FlaggedAtUtc = now,
                }
            );

        logger.LogInformation(
            "Offboarded user {UserId}: flagged {Count} office cloud connection(s) for review and discarded {States} pending connect attempt(s) in tenant {TenantId}.",
            msg.UserId,
            flagged.Count,
            discardedStates,
            msg.TenantId
        );
    }
}
