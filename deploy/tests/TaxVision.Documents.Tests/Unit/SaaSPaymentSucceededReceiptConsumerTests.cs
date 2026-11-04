using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Application.Generations.SaaSReceipt;
using Wolverine;
using Wolverine.Transports.Sending;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>El cobro SaaS confirmado pide su recibo con lo que viaja en el evento, "Billed to" incluido.</summary>
public sealed class SaaSPaymentSucceededReceiptConsumerTests
{
    [Fact]
    public async Task Maps_the_billed_to_of_the_event_into_the_receipt()
    {
        var bus = new CapturingMessageBus();
        var evt = Event() with
        {
            BilledTo = new SaaSBilledToData(
                "Core Tax Professionals LLC",
                "12-3456789",
                "1600 Main St, Miami, FL 33101"
            ),
        };

        await HandleAsync(evt, bus);

        var command = Assert.IsType<GenerateSaaSReceiptDocumentCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(evt.TenantId, command.TenantId);
        Assert.Equal(evt.SaaSPaymentId, command.SaaSPaymentId);
        Assert.Equal("CoreTaxPro", command.Receipt.OfficeName);
        Assert.Equal("Core Tax Professionals LLC", command.Receipt.BilledToLegalName);
        Assert.Equal("12-3456789", command.Receipt.BilledToEin);
        Assert.Equal("1600 Main St, Miami, FL 33101", command.Receipt.BilledToAddress);
    }

    [Fact]
    public async Task Without_billed_to_the_receipt_carries_only_the_office_name()
    {
        var bus = new CapturingMessageBus();

        await HandleAsync(Event(), bus);

        var command = Assert.IsType<GenerateSaaSReceiptDocumentCommand>(Assert.Single(bus.Invoked));
        Assert.Equal("CoreTaxPro", command.Receipt.OfficeName);
        Assert.Null(command.Receipt.BilledToLegalName);
        Assert.Null(command.Receipt.BilledToEin);
        Assert.Null(command.Receipt.BilledToAddress);
    }

    // El pago de un onboarding tiene su propio recibo, pedido por Auth.
    [Fact]
    public async Task A_payment_without_tenant_asks_for_nothing()
    {
        var bus = new CapturingMessageBus();

        await HandleAsync(Event() with { TenantId = Guid.Empty }, bus);

        Assert.Empty(bus.Invoked);
    }

    private static Task HandleAsync(SaaSPaymentSucceededIntegrationEvent evt, CapturingMessageBus bus) =>
        SaaSPaymentSucceededReceiptConsumer.Handle(
            evt,
            bus,
            new TestCorrelationContext("c"),
            NullLogger<GenerateSaaSReceiptDocumentResult>.Instance,
            CancellationToken.None
        );

    private static SaaSPaymentSucceededIntegrationEvent Event() =>
        new()
        {
            EventId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            SaaSPaymentId = Guid.NewGuid(),
            OfficeName = "CoreTaxPro",
            PaymentType = "SubscriptionRenewal",
            AmountPaidCents = 4900,
            Currency = "USD",
            PaidAtUtc = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc),
            ProviderReferenceMask = "4242",
        };

    private sealed class TestCorrelationContext(string correlationId) : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = correlationId;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            var previous = CorrelationId;
            CorrelationId = correlationId;
            return new Popper(() => CorrelationId = previous);
        }

        private sealed class Popper(Action pop) : IDisposable
        {
            public void Dispose() => pop();
        }
    }

    private sealed class CapturingMessageBus : IMessageBus
    {
        public List<object> Invoked { get; } = [];

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            Invoked.Add(message);
            var result = (object)Result.Success(new GenerateSaaSReceiptDocumentResult(Guid.NewGuid(), "Requested"));
            return Task.FromResult((T)result);
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
