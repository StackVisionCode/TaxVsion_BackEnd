using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Documents.Application.Generations.ClientFormSummary;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

public sealed class ClientFormSubmissionSummaryRequestedConsumerTests
{
    private static readonly byte[] MasterKey = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public async Task Requests_generation_for_a_valid_envelope()
    {
        var codec = Codec();
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var evt = Event(codec, tenantId, requestId, customerId);
        var bus = new CapturingMessageBus();

        await ClientFormSubmissionSummaryRequestedConsumer.Handle(
            evt,
            codec,
            bus,
            new TestCorrelationContext("c"),
            NullLogger<FormSubmissionSummaryRequestedIntegrationEvent>.Instance,
            default
        );

        var command = Assert.IsType<GenerateClientFormSummaryDocumentCommand>(Assert.Single(bus.Invoked));
        Assert.Equal(tenantId, command.TenantId);
        Assert.Equal(requestId, command.FormRequestId);
        Assert.Equal(evt.GenerationId, command.GenerationId);
    }

    [Fact]
    public async Task Discards_an_envelope_whose_customer_does_not_match_the_payload()
    {
        var codec = Codec();
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var evt = Event(codec, tenantId, requestId, Guid.NewGuid());
        // El sobre dice otro cliente que el ligado al cifrado: el binding ya no valida.
        var tampered = evt with
        {
            Participant = new ClientFormParticipant { Kind = "Customer", CustomerId = Guid.NewGuid() },
        };
        var bus = new CapturingMessageBus();

        await ClientFormSubmissionSummaryRequestedConsumer.Handle(
            tampered,
            codec,
            bus,
            new TestCorrelationContext("c"),
            NullLogger<FormSubmissionSummaryRequestedIntegrationEvent>.Instance,
            default
        );

        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Discards_an_envelope_for_another_tenant()
    {
        var codec = Codec();
        var requestId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var evt = Event(codec, Guid.NewGuid(), requestId, customerId);
        var tampered = evt with { TenantId = Guid.NewGuid() };
        var bus = new CapturingMessageBus();

        await ClientFormSubmissionSummaryRequestedConsumer.Handle(
            tampered,
            codec,
            bus,
            new TestCorrelationContext("c"),
            NullLogger<FormSubmissionSummaryRequestedIntegrationEvent>.Instance,
            default
        );

        Assert.Empty(bus.Invoked);
    }

    private static FormSubmissionSummaryRequestedIntegrationEvent Event(
        IProtectedPayloadCodec codec,
        Guid tenantId,
        Guid requestId,
        Guid customerId
    )
    {
        var participant = new ClientFormParticipant { Kind = "Customer", CustomerId = customerId };
        var idempotencyKey = $"client-form:{requestId:N}:r1";
        var binding = new ProtectedPayloadBinding(tenantId, requestId, participant, IdempotencyKey: idempotencyKey);
        var payload = new SubmissionSummaryPayload
        {
            TenantId = tenantId,
            CustomerId = customerId,
            FormRequestId = requestId,
            Snapshot = new SubmissionSummarySnapshot
            {
                Title = "T",
                VersionNumber = 1,
                RevisionNumber = 1,
            },
        };
        return new FormSubmissionSummaryRequestedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = tenantId,
            GenerationId = Guid.NewGuid(),
            FormRequestId = requestId,
            RevisionNumber = 1,
            Participant = participant,
            IdempotencyKey = idempotencyKey,
            Payload = codec.Protect(ClientFormProtectedPayloadSchemas.SubmissionSummary, binding, payload),
        };
    }

    private static JsonProtectedPayloadCodec Codec() =>
        new(
            new AesGcmRotatingSecretProtector(
                Options.Create(
                    new RotatingSecretProtectionOptions
                    {
                        MasterKey = Convert.ToBase64String(MasterKey),
                        MasterKeyVersion = 1,
                    }
                )
            )
        );

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
            var result = (object)
                Result.Success(new GenerateClientFormSummaryDocumentResult(Guid.NewGuid(), "Requested"));
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
