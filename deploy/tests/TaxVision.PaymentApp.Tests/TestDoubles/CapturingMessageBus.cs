using Wolverine;

namespace TaxVision.PaymentApp.Tests.TestDoubles;

/// <summary>Fake de <see cref="IMessageBus"/> que solo captura lo publicado; el resto lanza.</summary>
internal sealed class CapturingMessageBus : IMessageBus
{
    public List<object> Published { get; } = [];
    public string? TenantId { get; set; }

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        if (message is not null)
            Published.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotSupportedException();

    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
        throw new NotSupportedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotSupportedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        throw new NotSupportedException();

    public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotSupportedException();

    public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

    public Task InvokeForTenantAsync(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<T> InvokeForTenantAsync<T>(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotSupportedException();

    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotSupportedException();

    public Task<T> InvokeAsync<T>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        CancellationToken cancellation = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default
    ) => throw new NotSupportedException();
}
