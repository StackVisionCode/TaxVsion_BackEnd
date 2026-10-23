using System.Collections.Concurrent;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Captura los integration events publicados por el dispatcher/gateway en los tests.</summary>
public sealed class CapturingIntegrationEventPublisher : IIntegrationEventPublisher
{
    public ConcurrentBag<object> Published { get; } = new();

    public Task PublishAsync<T>(T message, CancellationToken ct = default)
        where T : class
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public List<(string Queue, object Message)> SentToQueue { get; } = [];

    public Task SendToQueueAsync<T>(string queueName, T message, CancellationToken ct = default)
        where T : class
    {
        SentToQueue.Add((queueName, message));
        return Task.CompletedTask;
    }
}
