using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Codes.Application.Reservations.CancelReservation;
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

    [Theory]
    [InlineData("Codes.CommitReservation.PaymentMismatch")]
    [InlineData("Codes.CommitReservation.SnapshotMismatch")]
    [InlineData("Codes.CommitReservation.CodeNotFound")]
    public async Task PaymentSucceeded_SnapshotMismatch_ThrowsBusinessRuleException(string code)
    {
        var bus = new FailingCommitBus(new Error(code, "Fixed rule."));

        var ex = await Assert.ThrowsAsync<GrowthBusinessRuleException>(() =>
            PaymentSucceededConsumer.Handle(NewEvent(), bus, CancellationToken.None)
        );

        Assert.Equal(code, ex.ErrorCode);
    }

    [Fact]
    public async Task PaymentSucceeded_DatabaseTimeout_ThrowsFinancialEventException()
    {
        var bus = new FailingCommitBus(new Error("Growth.Database.Timeout", "Timeout."));

        await Assert.ThrowsAsync<GrowthFinancialEventException>(() =>
            PaymentSucceededConsumer.Handle(NewEvent(), bus, CancellationToken.None)
        );
    }

    [Fact]
    public async Task PaymentSucceeded_UnknownPaymentSource_ThrowsBusinessRuleException()
    {
        var message = NewEvent() with
        {
            CodeReservationId = null,
            ReferralAttributionId = Guid.NewGuid(),
            PaymentSource = "Barter",
        };

        var ex = await Assert.ThrowsAsync<GrowthBusinessRuleException>(() =>
            PaymentSucceededConsumer.Handle(message, new FailingCommitBus(new Error("Unused", "Unused.")), default)
        );

        Assert.Equal("Growth.PaymentEvent.InvalidSource", ex.ErrorCode);
    }

    [Fact]
    public async Task PaymentSucceeded_ReservationWithoutSnapshot_ThrowsBusinessRuleException()
    {
        var message = NewEvent() with { PromotionSnapshotHash = " " };

        var ex = await Assert.ThrowsAsync<GrowthBusinessRuleException>(() =>
            PaymentSucceededConsumer.Handle(message, new FailingCommitBus(new Error("Unused", "Unused.")), default)
        );

        Assert.Equal("Growth.PaymentEvent.SnapshotRequired", ex.ErrorCode);
    }

    [Theory]
    [InlineData("Codes.CancelReservation.PaymentMismatch", true)]
    [InlineData("Codes.CancelReservation.CodeNotFound", true)]
    [InlineData("Growth.Idempotency.OperationInProgress", false)]
    public async Task PaymentCancelled_ClassifiesTheFailure(string code, bool isBusinessRule)
    {
        var bus = new FailingCancelBus(new Error(code, "Failed."));

        var ex = await Record.ExceptionAsync(() => PaymentCancelledConsumer.Handle(NewCancelled(), bus, default));

        Assert.IsType(isBusinessRule ? typeof(GrowthBusinessRuleException) : typeof(GrowthFinancialEventException), ex);
    }

    // Los códigos de la lista existen: si un handler los renombra, volverían a reintentarse en silencio.
    [Fact]
    public void Classification_UsesCodesThatTheHandlersReallyReturn()
    {
        var root = RepositoryRoot();
        var handlers =
            File.ReadAllText(
                Path.Combine(
                    root,
                    "TaxVision.Codes.Application/Reservations/CommitReservation/CommitReservationHandler.cs"
                )
            )
            + File.ReadAllText(
                Path.Combine(
                    root,
                    "TaxVision.Codes.Application/Reservations/CancelReservation/CancelReservationHandler.cs"
                )
            );

        foreach (
            var code in new[]
            {
                "Codes.CommitReservation.PaymentMismatch",
                "Codes.CommitReservation.SnapshotMismatch",
                "Codes.CommitReservation.CodeNotFound",
                "Codes.CancelReservation.PaymentMismatch",
                "Codes.CancelReservation.CodeNotFound",
            }
        )
        {
            Assert.Contains($"\"{code}\"", handlers);
            Assert.True(GrowthErrorClassification.IsBusinessRule(code), code);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "Services", "Growth");
    }

    private static PaymentCancelledIntegrationEvent NewCancelled() =>
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
            ReasonCode = "cancelled",
            CancelledAtUtc = DateTime.UtcNow,
        };

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

    private sealed class FailingCancelBus(Error error) : FailingCommitBus(error)
    {
        public override Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => Task.FromResult((T)(object)Result.Failure<CancelReservationResponse>(Error));
    }

    /// <summary>Bus falso: el commit devuelve el error dado.</summary>
    private class FailingCommitBus(Error error) : IMessageBus
    {
        protected Error Error { get; } = error;

        public string? TenantId { get; set; }

        public virtual Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            Assert.IsType<CommitReservationCommand>(message);
            return Task.FromResult((T)(object)Result.Failure<CommitReservationResponse>(Error));
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
