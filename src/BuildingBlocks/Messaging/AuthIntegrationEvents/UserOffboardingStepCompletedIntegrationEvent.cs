using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Acuse de un paso del retiro (offboard): un consumer de <see cref="UserOffboardedIntegrationEvent"/> terminó
/// con éxito y ya persistió. En .NET lo publica sola la política común de Wolverine
/// (<c>OffboardingStepAcknowledgementPolicy</c>); Communication (Node) lo publica al final de su consumer con
/// el mismo alias. Auth lo registra en el <c>OffboardingRun</c> del usuario (idempotente por Service+Step).
/// </summary>
[MessageIdentity(MessageType)]
public sealed record UserOffboardingStepCompletedIntegrationEvent : IntegrationEvent
{
    /// <summary>Alias estable del tipo (header AMQP <c>type</c>): lo usan Node y .NET por igual.</summary>
    public const string MessageType = "auth.user.offboarding_step_completed.v1";

    /// <summary>Usuario retirado.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Servicio que hizo el paso (carpeta de src/Services, p. ej. "Calendar").</summary>
    public required string Service { get; init; }

    /// <summary>Consumer que lo hizo (clase .NET o archivo .ts sin extensión).</summary>
    public required string Step { get; init; }

    public required DateTime CompletedAtUtc { get; init; }
}
