using TaxVision.Auth.Application.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Application.Sessions;

/// <summary>Cierra una sesión vencida (sin ningún refresh vigente) y avisa en la misma unidad.</summary>
public static class ExpiredSessionSweep
{
    public const string Reason = "expired";

    /// <summary>Sesiones por lote del barrido.</summary>
    public const int BatchSize = 200;

    /// <summary>
    /// Una sesión por unidad: la marca y publica por el outbox de la unidad, que confirma los dos juntos.
    /// Devuelve false si ya estaba cerrada (otra réplica o un logout le ganó): no se repite el aviso.
    /// </summary>
    public static async Task<bool> CloseAsync(
        Guid sessionId,
        ISessionRepository sessions,
        IMessageBus bus,
        DateTime now,
        CancellationToken ct
    )
    {
        var session = await sessions.GetSessionByIdAsync(sessionId, ct);
        if (session is null || !session.IsActive)
            return false;

        await sessions.RevokeSessionAsync(session.Id, Reason, ct);
        await bus.PublishAsync(SessionEndedEvents.ForSession(session, Reason, now));
        return true;
    }
}
