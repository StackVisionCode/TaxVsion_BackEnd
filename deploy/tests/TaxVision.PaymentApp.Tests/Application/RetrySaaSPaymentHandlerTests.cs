using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RetrySaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Un reintento sin tarjeta guardada no llama al provider y no se vuelve a agendar.</summary>
public sealed class RetrySaaSPaymentHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task Without_a_saved_card_the_retry_never_reaches_the_provider_and_stops_dunning()
    {
        var payment = DueFailedPayment();
        var payments = new FakeSaaSPaymentRepository { ById = payment };
        var bus = new CapturingMessageBus();

        // FakePaymentProvider lanza si se le pide crear customer o cobrar: el test falla si se llama.
        var result = await RetrySaaSPaymentHandler.Handle(
            new RetrySaaSPaymentCommand(_tenantId, payment.Id, Guid.Empty),
            payments,
            new NoSavedCustomers(),
            new FakePaymentAdapterFactory(new FakePaymentProvider()),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            bus,
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(SaaSPaymentChargeOutcome.MissingPaymentMethodCode, payment.FailureCode);
        Assert.Null(payment.NextRetryAtUtc);
        var failed = Assert.Single(bus.Published.OfType<SubscriptionRenewalPaymentFailedIntegrationEvent>());
        Assert.False(failed.WillRetry);
    }

    [Theory]
    [InlineData("requires_payment_method")]
    [InlineData("PayPal.Authorize.NotImplemented")]
    public void Permanent_failures_are_not_retried(string failureCode)
    {
        var payment = RetryingPayment();

        SaaSPaymentChargeOutcome.FailPayment(
            payment,
            new Error(failureCode, "Cannot charge automatically."),
            Guid.Empty,
            DateTime.UtcNow.AddHours(6),
            new FakePaymentAppMetrics()
        );

        Assert.Null(payment.NextRetryAtUtc);
    }

    [Fact]
    public void A_card_decline_is_still_retried()
    {
        var payment = RetryingPayment();

        SaaSPaymentChargeOutcome.FailPayment(
            payment,
            new Error("card_declined", "Declined."),
            Guid.Empty,
            DateTime.UtcNow.AddHours(6),
            new FakePaymentAppMetrics()
        );

        Assert.NotNull(payment.NextRetryAtUtc);
    }

    private SaaSPayment RetryingPayment()
    {
        var payment = DueFailedPayment();
        payment.PrepareForRetry(Guid.Empty, DateTime.UtcNow);
        return payment;
    }

    private SaaSPayment DueFailedPayment()
    {
        var payment = SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"subscription-renewal-{Guid.NewGuid():N}-20261101").Value,
                Money.Create(2900, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        payment.MarkFailed(
            "card_declined",
            "Declined.",
            true,
            DateTime.UtcNow.AddMinutes(-1),
            Guid.Empty,
            DateTime.UtcNow
        );
        return payment;
    }

    private sealed class NoSavedCustomers : ITenantProviderCustomerRepository
    {
        public Task<TenantProviderCustomer?> GetByTenantAndProviderAsync(
            Guid tenantId,
            PaymentProviderCode code,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(null);

        public Task<TenantProviderCustomer?> GetByIdAsync(
            Guid tenantProviderCustomerId,
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<TenantProviderCustomer?>(null);

        public Task<IReadOnlyList<TenantProviderCustomer>> GetWithMethodsExpiringBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantProviderCustomer>>([]);

        public Task AddAsync(TenantProviderCustomer added, CancellationToken ct = default) => Task.CompletedTask;
    }
}
