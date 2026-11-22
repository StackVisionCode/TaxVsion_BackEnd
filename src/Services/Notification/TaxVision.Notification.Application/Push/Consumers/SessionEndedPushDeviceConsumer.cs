using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Push.Consumers;

/// <summary>
/// <c>auth.session.ended.v1</c>: apaga el push registrado con esa sesión, aunque el navegador no haya podido
/// avisar (equipo compartido, sesión revocada desde otro lado). Revoca por estado: repetir el evento no cambia
/// nada, y un token que otra sesión volvió a registrar ya no lleva la sesión vieja. No es un paso del retiro.
/// </summary>
public static class SessionEndedPushDeviceConsumer
{
    public static async Task Handle(
        SessionEndedIntegrationEvent evt,
        IPushDeviceTokenRepository devices,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (evt.TenantId == Guid.Empty || evt.UserId == Guid.Empty)
            return;

        var revoked = evt.SessionId is { } sessionId
            ? await devices.RevokeForSessionAsync(evt.TenantId, evt.UserId, sessionId, ct)
            : await devices.RevokeAllActiveForUserExceptSessionAsync(evt.TenantId, evt.UserId, evt.ExceptSessionId, ct);
        if (revoked > 0)
            await unitOfWork.SaveChangesAsync(ct);
    }
}
