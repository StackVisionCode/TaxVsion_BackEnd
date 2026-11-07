using Wolverine;

namespace TaxVision.Growth.Tests.Application.Fakes;

/// <summary>Bus falso que resuelve <c>InvokeAsync</c> con el handler real y anota el tenant con que se invocó.</summary>
internal sealed class RoutingMessageBus(Func<object, string?, object> route) : IMessageBus
{
    public string? TenantId { get; set; }
    public List<(object Message, string? TenantId)> Invoked { get; } = [];

    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        Invoked.Add((message, TenantId));
        return (Task<T>)route(message, TenantId);
    }

    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotSupportedException();

    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

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

    public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotSupportedException();

    public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotSupportedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        throw new NotSupportedException();

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotSupportedException();

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) => throw new NotSupportedException();

    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
        throw new NotSupportedException();
}
