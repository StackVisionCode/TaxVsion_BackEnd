using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Infrastructure.Sms;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Notification.Tests;

/// <summary>
/// P3 — el puente SMS publica <see cref="SmsSendRequestedIntegrationEvent"/> con teléfono, cuerpo,
/// tenant del contexto y un CustomerId/idempotencia derivados; y rechaza sin destino o sin tenant.
/// </summary>
public sealed class IntegrationEventSmsSenderTests
{
    [Fact]
    public async Task Publishes_sms_request_with_tenant_and_derived_identity()
    {
        var tenantId = Guid.NewGuid();
        var bus = new CapturingBus();
        var sender = new IntegrationEventSmsSender(
            bus,
            new StubTenant(tenantId),
            new NoOpCorrelation(),
            NullLogger<IntegrationEventSmsSender>.Instance
        );

        var result = await sender.SendAsync("+17865550123", "Your code: 123456");

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<SmsSendRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(tenantId, evt.TenantId);
        Assert.Equal("+17865550123", evt.To);
        Assert.Equal("Your code: 123456", evt.Body);
        Assert.NotEqual(Guid.Empty, evt.CustomerId);
        Assert.False(string.IsNullOrWhiteSpace(evt.IdempotencyKey));
    }

    [Fact]
    public async Task Same_phone_and_text_derive_stable_idempotency_and_customer()
    {
        var tenantId = Guid.NewGuid();
        SmsSendRequestedIntegrationEvent Send()
        {
            var bus = new CapturingBus();
            var sender = new IntegrationEventSmsSender(
                bus,
                new StubTenant(tenantId),
                new NoOpCorrelation(),
                NullLogger<IntegrationEventSmsSender>.Instance
            );
            sender.SendAsync("+17865550123", "Your code: 123456").GetAwaiter().GetResult();
            return Assert.IsType<SmsSendRequestedIntegrationEvent>(Assert.Single(bus.Published));
        }

        var first = Send();
        var second = Send();
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(first.CustomerId, second.CustomerId);
    }

    [Fact]
    public async Task Fails_when_no_destination()
    {
        var sender = new IntegrationEventSmsSender(
            new CapturingBus(),
            new StubTenant(Guid.NewGuid()),
            new NoOpCorrelation(),
            NullLogger<IntegrationEventSmsSender>.Instance
        );

        var result = await sender.SendAsync("  ", "body");

        Assert.True(result.IsFailure);
        Assert.Equal("Notification.Sms.NoDestination", result.Error.Code);
    }

    [Fact]
    public async Task Fails_when_no_tenant_context()
    {
        var bus = new CapturingBus();
        var sender = new IntegrationEventSmsSender(
            bus,
            new StubTenant(hasTenant: false),
            new NoOpCorrelation(),
            NullLogger<IntegrationEventSmsSender>.Instance
        );

        var result = await sender.SendAsync("+17865550123", "body");

        Assert.True(result.IsFailure);
        Assert.Equal("Notification.Sms.NoTenant", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    // ---------------- fakes ----------------

    private sealed class StubTenant : ITenantContext
    {
        private Guid _tenantId;

        public StubTenant(Guid tenantId)
        {
            _tenantId = tenantId;
            HasTenant = true;
        }

        public StubTenant(bool hasTenant) => HasTenant = hasTenant;

        public Guid TenantId => _tenantId;
        public bool HasTenant { get; private set; }

        public void SetTenant(Guid tenantId)
        {
            _tenantId = tenantId;
            HasTenant = true;
        }
    }

    private sealed class NoOpCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class CapturingBus : IMessageBus
    {
        public List<object> Published { get; } = [];

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            if (message is not null)
                Published.Add(message);
            return ValueTask.CompletedTask;
        }

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
