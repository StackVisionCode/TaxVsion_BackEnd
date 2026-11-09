using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>Cancelación de un PaymentIntent abandonado (D-PCL-1) contra un stub HTTP: sin red.</summary>
public sealed class StripeCancelChargeTests
{
    private static readonly TenantProviderCredentials Credentials = new("sk_test_office", null);

    [Fact]
    public async Task A_requires_action_intent_is_cancelled_in_the_connected_account()
    {
        var stripe = new StripeStub
        {
            ["POST /v1/payment_intents/pi_1/cancel"] = (
                HttpStatusCode.OK,
                """{ "id": "pi_1", "object": "payment_intent", "status": "canceled", "cancellation_reason": "abandoned" }"""
            ),
        };

        var result = await Adapter(stripe)
            .CancelChargeAsync(Credentials, new ChargeStatusLookup("pi_1", "acct_office"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Cancelled, result.Value.Status);
        Assert.Equal("pi_1", result.Value.ProviderChargeReference);
        Assert.Contains("cancellation_reason=abandoned", stripe.Bodies["POST /v1/payment_intents/pi_1/cancel"]);
        Assert.Equal("acct_office", stripe.StripeAccount);
    }

    [Fact]
    public async Task A_cancel_rejected_because_the_intent_succeeded_meanwhile_is_reported_paid()
    {
        var stripe = new StripeStub
        {
            ["POST /v1/payment_intents/pi_1/cancel"] = (
                HttpStatusCode.BadRequest,
                """
                { "error": { "type": "invalid_request_error", "code": "payment_intent_unexpected_state",
                  "message": "You cannot cancel this PaymentIntent because it has a status of succeeded." } }
                """
            ),
            ["GET /v1/payment_intents/pi_1"] = (
                HttpStatusCode.OK,
                """{ "id": "pi_1", "object": "payment_intent", "status": "succeeded", "amount_received": 2500, "currency": "usd" }"""
            ),
        };

        var result = await Adapter(stripe).CancelChargeAsync(Credentials, new ChargeStatusLookup("pi_1"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, result.Value.Status);
        Assert.Equal(2500, result.Value.PaidAmountCents);
        Assert.Equal("usd", result.Value.PaidCurrency);
        Assert.Equal(["POST /v1/payment_intents/pi_1/cancel", "GET /v1/payment_intents/pi_1"], stripe.Requested);
    }

    [Fact]
    public async Task A_Stripe_error_on_cancel_is_a_failure_to_retry_later()
    {
        var stripe = new StripeStub
        {
            ["POST /v1/payment_intents/pi_1/cancel"] = (
                HttpStatusCode.InternalServerError,
                """{ "error": { "type": "api_error", "message": "boom" } }"""
            ),
        };

        var result = await Adapter(stripe).CancelChargeAsync(Credentials, new ChargeStatusLookup("pi_1"), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Stripe.CancelCharge.Failed", result.Error.Code);
    }

    private static StripePaymentAdapter Adapter(StripeStub stripe) =>
        new(NullLogger<StripePaymentAdapter>.Instance, new StubClientFactory(stripe));

    private sealed class StubClientFactory(HttpMessageHandler handler) : IStripeClientFactory
    {
        public IStripeClient Create(string secretKey) =>
            new StripeClient(
                secretKey,
                httpClient: new SystemNetHttpClient(new HttpClient(handler), maxNetworkRetries: 0)
            );
    }

    /// <summary>Responde por "MÉTODO ruta"; anota lo pedido, los cuerpos y el header Stripe-Account.</summary>
    private sealed class StripeStub : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes = [];

        public (HttpStatusCode Status, string Body) this[string route]
        {
            set => _routes[route] = value;
        }

        public List<string> Requested { get; } = [];
        public Dictionary<string, string> Bodies { get; } = [];
        public string? StripeAccount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var route = $"{request.Method.Method} {request.RequestUri!.AbsolutePath}";
            Requested.Add(route);
            if (request.Content is not null)
                Bodies[route] = Uri.UnescapeDataString(await request.Content.ReadAsStringAsync(ct));
            if (request.Headers.TryGetValues("Stripe-Account", out var account))
                StripeAccount = account.Single();

            var (status, body) = _routes.TryGetValue(route, out var r)
                ? r
                : (HttpStatusCode.NotFound, """{ "error": { "type": "invalid_request_error" } }""");
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
