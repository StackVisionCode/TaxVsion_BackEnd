using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Terminó una sesión (logout, revocación, cambio de contraseña, takeover o vencimiento). Solo ids: Notification
/// revoca el push de esa sesión. <c>TenantId</c> del sobre = oficina de la sesión.
/// </summary>
[MessageIdentity("auth.session.ended.v1")]
public sealed record SessionEndedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    /// <summary>Sesión terminada. Null = todas las del usuario (ver <see cref="ExceptSessionId"/>).</summary>
    public Guid? SessionId { get; init; }

    /// <summary>Con <see cref="SessionId"/> null: la sesión que sigue viva (cambio de contraseña, "cerrar las demás").</summary>
    public Guid? ExceptSessionId { get; init; }

    /// <summary>Motivo de la revocación (lista cerrada de Auth) o "expired".</summary>
    public required string Reason { get; init; }

    /// <summary>"Web" | "Mobile" cuando se conoce.</summary>
    public string? Channel { get; init; }

    public required DateTime EndedAtUtc { get; init; }
}
