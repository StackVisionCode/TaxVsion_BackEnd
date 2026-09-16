using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentApp.Tests.Infrastructure;

public sealed class StripePaymentAdapterWebhookTests
{
    private static StripePaymentAdapter CreateAdapter() =>
        new(
            Options.Create(new StripeOptions { SecretKey = "sk_test_dummy", WebhookSecret = "whsec_dummy" }),
            NullLogger<StripePaymentAdapter>.Instance,
            new NoOpMetrics()
        );

    [Fact]
    public async Task Checkout_session_expired_maps_to_failed_and_resolves_by_session_id()
    {
        // Onboarding abandonado que nunca vuelve al front (sin reconcile): el evento se resuelve por el
        // id de Session -- la referencia provisoria que se guarda al crear la sesión sin PaymentIntent --
        // y se marca Failed, así el comprador recibe el aviso con link de reintento.
        const string payload = """
            {
              "id": "evt_test_expired",
              "object": "event",
              "api_version": "2024-06-20",
              "created": 1700000000,
              "livemode": false,
              "pending_webhooks": 1,
              "request": { "id": null, "idempotency_key": null },
              "type": "checkout.session.expired",
              "data": {
                "object": {
                  "id": "cs_test_abandoned",
                  "object": "checkout.session",
                  "status": "expired",
                  "payment_status": "unpaid",
                  "livemode": false
                }
              }
            }
            """;

        var result = await CreateAdapter()
            .ParseWebhookEventAsync(payload, "checkout.session.expired", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("cs_test_abandoned", result.Value.ProviderChargeReference);
        Assert.Equal(PaymentStatus.Failed, result.Value.Status);
        Assert.Equal("Stripe.CheckoutSession.Expired", result.Value.FailureCode);
    }

    private sealed class NoOpMetrics : IPaymentAppMetrics
    {
        public void RecordAttempted(string provider, string type) { }

        public void RecordSucceeded(string provider, string type) { }

        public void RecordFailed(string provider, string type, string failureCode) { }

        public void RecordRefunded(string provider) { }

        public void RecordChargedBack(string provider) { }

        public void RecordWebhookReceived(string provider) { }

        public void RecordWebhookDuplicate(string provider) { }

        public void RecordWebhookSignatureFailed(string provider) { }

        public void RecordProviderLatency(double milliseconds, string provider, string method) { }
    }
}
