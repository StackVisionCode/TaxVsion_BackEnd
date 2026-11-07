using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Wolverine;

namespace TaxVision.Signature.Application.Delivery;

/// <summary>
/// BB-17. <see cref="SignerPartialCopyConsumer"/> espera el escaneo dentro de su handler: si el pedido saliera por su
/// outbox, no saldría hasta que termine y el resultado nunca llegaría. Se invoca, y sale en una unidad propia.
/// </summary>
public sealed record RequestPartialCopyScanCommand(Guid TenantId, SaveFileRequestedIntegrationEvent Request);

public static class RequestPartialCopyScanHandler
{
    public static ValueTask Handle(RequestPartialCopyScanCommand command, IMessageBus bus) =>
        bus.PublishAsync(command.Request);
}
