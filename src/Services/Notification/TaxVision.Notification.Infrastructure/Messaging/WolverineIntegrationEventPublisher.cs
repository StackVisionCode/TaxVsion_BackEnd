using TaxVision.Notification.Application.Abstractions;
using Wolverine;

namespace TaxVision.Notification.Infrastructure.Messaging;

/// <summary>Implementación de <see cref="IIntegrationEventPublisher"/> que delega en Wolverine.</summary>
public sealed class WolverineIntegrationEventPublisher(IMessageBus bus) : IIntegrationEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken ct = default)
        where T : class => bus.PublishAsync(message).AsTask();

    // Mismo contexto del bus: con la transacción abierta el envío entra al outbox durable.
    public Task SendToQueueAsync<T>(string queueName, T message, CancellationToken ct = default)
        where T : class => bus.EndpointFor(new Uri($"rabbitmq://queue/{queueName}")).SendAsync(message).AsTask();
}
