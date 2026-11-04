using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Push.Consumers;

/// <summary>
/// Al suspender o retirar a un usuario se revocan todos sus dispositivos push: sin esto el teléfono
/// seguiría recibiendo avisos con datos de clientes. Retiro ⊇ suspensión. Al reactivar no se
/// restauran: la app vuelve a registrar el token al iniciar sesión. Idempotente.
/// </summary>
public static class UserLifecyclePushDeviceConsumer
{
    public static Task Handle(
        UserDeactivatedIntegrationEvent evt,
        IPushDeviceTokenRepository devices,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<PushDeviceToken> logger,
        CancellationToken ct
    ) =>
        RevokeAllAsync(
            evt.TenantId,
            evt.UserId,
            Correlation.From(evt.CorrelationId, evt.EventId),
            devices,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        UserOffboardedIntegrationEvent evt,
        IPushDeviceTokenRepository devices,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<PushDeviceToken> logger,
        CancellationToken ct
    ) =>
        RevokeAllAsync(
            evt.TenantId,
            evt.UserId,
            Correlation.From(evt.CorrelationId, evt.EventId),
            devices,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    private static async Task RevokeAllAsync(
        Guid tenantId,
        Guid userId,
        string correlationId,
        IPushDeviceTokenRepository devices,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<PushDeviceToken> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(correlationId))
        {
            var revoked = await devices.RevokeAllActiveForUserAsync(tenantId, userId, ct);
            if (revoked == 0)
                return;

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Revoked {Count} push device(s) of user {UserId} in tenant {TenantId} after the account lost access.",
                revoked,
                userId,
                tenantId
            );
        }
    }
}
