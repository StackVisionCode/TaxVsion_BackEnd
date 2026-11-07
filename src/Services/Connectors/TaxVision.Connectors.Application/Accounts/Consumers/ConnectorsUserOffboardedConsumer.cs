using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Wolverine;

namespace TaxVision.Connectors.Application.Accounts.Consumers;

/// <summary>
/// Al RETIRAR (offboard) a un empleado, sus buzones PERSONALES se desconectan y purgan
/// (<see cref="PurgeOffboardedMailboxesHandler"/>). Los de OFICINA no se tocan aquí (ver
/// <see cref="OfficeMailboxesUserOffboardedConsumer"/>).
/// <para>Solo reenvía: la purga revoca en el proveedor y debe correr sin transacción, y este mensaje comparte handler
/// con otros que sí la necesitan. El comando sale por el outbox de esa transacción.</para>
/// </summary>
public static class ConnectorsUserOffboardedConsumer
{
    public static ValueTask Handle(UserOffboardedIntegrationEvent msg, IMessageBus bus) =>
        bus.PublishAsync(
            new PurgeOffboardedMailboxesCommand(
                msg.TenantId,
                msg.UserId,
                string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
            )
        );
}
