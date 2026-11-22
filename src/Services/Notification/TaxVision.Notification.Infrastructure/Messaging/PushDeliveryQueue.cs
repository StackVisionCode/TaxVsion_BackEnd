using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Infrastructure.Messaging;

/// <summary>Publica en el bus del handler: el envío solo sale si el consumer confirma.</summary>
public sealed class PushDeliveryQueue(HandlerBus bus) : IPushDeliveryQueue
{
    public async Task EnqueueAsync(DeliverPush command, CancellationToken ct = default) =>
        await bus.Current.PublishAsync(command);
}
