using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Messages.Consumers;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Sms.Tests;

/// <summary>
/// P3 — el consumer genérico traduce <see cref="SmsSendRequestedIntegrationEvent"/> a un
/// <see cref="SendSmsBatchCommand"/> de un solo ítem, pasando tal cual CustomerId e IdempotencyKey
/// (ya vienen resueltos en el evento), sin derivarlos.
/// </summary>
public sealed class SmsSendRequestedConsumerTests
{
    [Fact]
    public async Task Maps_event_to_single_item_batch_command()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var bus = new InvokingBus();

        var evt = new SmsSendRequestedIntegrationEvent
        {
            TenantId = tenantId,
            CorrelationId = "corr-1",
            To = "+17865550123",
            Body = "Your code: 654321",
            CustomerId = customerId,
            IdempotencyKey = "idem-key-1",
            SourceContext = "signature:req:signer:otp",
        };

        await SmsSendRequestedConsumer.Handle(evt, bus, CancellationToken.None);

        var command = Assert.IsType<SendSmsBatchCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(tenantId, command.TenantId);
        Assert.Equal("corr-1", command.CorrelationId);
        var item = Assert.Single(command.Items);
        Assert.Equal(customerId, item.CustomerId);
        Assert.Equal("+17865550123", item.To);
        Assert.Equal("Your code: 654321", item.Message);
        Assert.Equal("idem-key-1", item.IdempotencyKey);
        Assert.Equal("signature:req:signer:otp", item.SourceContext);
        Assert.Null(item.Media);
    }

    private sealed class InvokingBus : IMessageBus
    {
        public List<object> Invoked { get; } = [];

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            Invoked.Add(message);
            var response = new SendSmsBatchResponse(Guid.NewGuid(), "corr-1", []);
            return Task.FromResult((T)(object)Result.Success(response));
        }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) => ValueTask.CompletedTask;

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public string? TenantId { get; set; }

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();
    }
}
