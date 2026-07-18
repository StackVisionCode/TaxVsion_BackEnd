using TaxVision.Notification.Application.Abstractions;
using Wolverine;

namespace TaxVision.Notification.Infrastructure.Messaging;

/// <summary>Implementación de <see cref="IIntegrationEventPublisher"/> que delega en Wolverine.</summary>
public sealed class WolverineIntegrationEventPublisher(IMessageBus bus) : IIntegrationEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken ct = default)
        where T : class => bus.PublishAsync(message).AsTask();
}
