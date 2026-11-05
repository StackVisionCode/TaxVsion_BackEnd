using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Codes.Application.Reservations.CommitReservation;
using TaxVision.Growth.Api.IntegrationEvents;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Growth.Tests.Application;

public sealed class PaymentLifecycleConsumersTests
{
    [Fact]
    public async Task PaymentSucceeded_LateCommitPendingReview_ThrowsGrowthBusinessRuleException()
    {
        var bus = new FailingCommitBus(
            new Error("Codes.CommitReservation.LateCommitPendingReview", "Late commit pending review.")
        );

        var ex = await Assert.ThrowsAsync<GrowthBusinessRuleException>(() =>
            PaymentSucceededConsumer.Handle(NewEvent(), bus, CancellationToken.None)
        );

        Assert.Equal("Codes.CommitReservation.LateCommitPendingReview", ex.ErrorCode);
    }

    [Fact]
    public async Task PaymentSucceeded_OtherCommitFailure_StillThrowsRetryableException()
    {
        var bus = new FailingCommitBus(new Error("Growth.Idempotency.OperationInProgress", "In progress."));

        await Assert.ThrowsAsync<GrowthFinancialEventException>(() =>
            PaymentSucceededConsumer.Handle(NewEvent(), bus, CancellationToken.None)
        );
    }

    private static PaymentSucceededIntegrationEvent NewEvent() =>
        new()
        {
            TenantId = Guid.NewGuid(),
            AggregateId = Guid.NewGuid(),
            AggregateVersion = 1,
            PaymentSource = "PaymentApp",
            PaymentId = Guid.NewGuid(),
            GrossAmountCents = 10_000,
            DiscountAmountCents = 1_000,
            NetAmountCents = 9_000,
            Currency = "USD",
            CodeReservationId = Guid.NewGuid(),
            PromotionSnapshotHash = new string('a', 64),
            IsFirstSuccessfulPayment = true,
            PaidAtUtc = DateTime.UtcNow,
        };

    /// <summary>Bus falso: el commit devuelve el error dado.</summary>
    private sealed class FailingCommitBus(Error error) : IMessageBus
    {
        public string? TenantId { get; set; }

        public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
        {
            Assert.IsType<CommitReservationCommand>(message);
            return Task.FromResult((T)(object)Result.Failure<CommitReservationResponse>(error));
        }

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

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotImplementedException();

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();
    }
}
