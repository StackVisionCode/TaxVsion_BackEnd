using Wolverine;

namespace TaxVision.BuildingBlocks.Tests.Tenancy;

internal sealed class FakeMessageBus : IMessageBus
{
    public string? TenantId { get; set; }

    public string? CorrelationId
    {
        get => null;
        set { }
    }

    public string ConversationId => string.Empty;

    public string PersistenceGroup => string.Empty;

    public IReadOnlyList<Envelope> Outstanding => Array.Empty<Envelope>();

    public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotSupportedException();

    public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotSupportedException();

    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotSupportedException();

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) => throw new NotSupportedException();

    public ValueTask<Guid> ScheduleAsync<T>(T message, DateTimeOffset scheduledTime, DeliveryOptions? options = null) =>
        throw new NotSupportedException();

    public ValueTask<Guid> ScheduleAsync<T>(T message, TimeSpan delay, DeliveryOptions? options = null) =>
        throw new NotSupportedException();

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotSupportedException();

    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
        throw new NotSupportedException();

    public ValueTask<T> InvokeAsync<T>(
        Uri destination,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public ValueTask SendAsync<T>(Uri destination, T message, DeliveryOptions? options = null) =>
        throw new NotSupportedException();

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

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotSupportedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
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
}
