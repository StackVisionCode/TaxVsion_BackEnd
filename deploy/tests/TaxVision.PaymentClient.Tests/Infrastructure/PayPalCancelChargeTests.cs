using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Infrastructure.Providers.PayPal;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>Cierre de un cobro PayPal abandonado (D-PCL-1) contra un stub HTTP: nunca captura; si ya se
/// cobró lo informa, si hay una autorización la anula y si no, la orden queda sin cobrar.</summary>
public sealed class PayPalCancelChargeTests
{
    private const string Base = "https://api-m.sandbox.paypal.com";

    private static readonly TenantProviderCredentials Credentials = new(
        SecretKey: "secret",
        WebhookSecret: null,
        PublishableKey: "client-id",
        ApiBaseUrl: Base
    );

    [Fact]
    public async Task A_completed_capture_is_reported_paid_and_not_cancelled()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/CAP-1"] = Ok(
                """{ "id": "CAP-1", "status": "COMPLETED", "amount": { "value": "25.00", "currency_code": "USD" } }"""
            ),
        };

        var result = await CancelAsync(handler, "CAP-1");

        Assert.Equal(PaymentStatus.Succeeded, result.Value.Status);
        Assert.Equal(2500, result.Value.PaidAmountCents);
        Assert.DoesNotContain(handler.Requested, r => r.EndsWith("/void") || r.EndsWith("/capture"));
    }

    [Fact]
    public async Task An_approved_order_without_capture_is_closed_without_capturing()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/ORDER-1"] = (HttpStatusCode.NotFound, "{}"),
            ["/v2/checkout/orders/ORDER-1"] = Ok("""{ "id": "ORDER-1", "status": "APPROVED" }"""),
        };

        var result = await CancelAsync(handler, "ORDER-1");

        Assert.Equal(PaymentStatus.Cancelled, result.Value.Status);
        Assert.Equal("ORDER-1", result.Value.ProviderChargeReference);
        Assert.DoesNotContain(handler.Requested, r => r.EndsWith("/capture"));
    }

    [Fact]
    public async Task An_order_with_an_open_authorization_voids_it()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/ORDER-1"] = (HttpStatusCode.NotFound, "{}"),
            ["/v2/checkout/orders/ORDER-1"] = Ok(
                """
                { "id": "ORDER-1", "status": "COMPLETED", "purchase_units": [ { "payments": { "authorizations": [
                  { "id": "AUTH-1", "status": "CREATED" }
                ] } } ] }
                """
            ),
            ["/v2/payments/authorizations/AUTH-1/void"] = (HttpStatusCode.NoContent, ""),
        };

        var result = await CancelAsync(handler, "ORDER-1");

        Assert.Equal(PaymentStatus.Cancelled, result.Value.Status);
        Assert.Contains("POST /v2/payments/authorizations/AUTH-1/void", handler.Requested);
    }

    [Fact]
    public async Task A_pending_capture_is_not_closed()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/CAP-1"] = Ok("""{ "id": "CAP-1", "status": "PENDING" }"""),
        };

        var result = await CancelAsync(handler, "CAP-1");

        Assert.Equal(PaymentStatus.Processing, result.Value.Status);
    }

    [Fact]
    public async Task A_failed_void_is_a_failure_to_retry_later()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/ORDER-1"] = (HttpStatusCode.NotFound, "{}"),
            ["/v2/checkout/orders/ORDER-1"] = Ok(
                """
                { "id": "ORDER-1", "status": "COMPLETED", "purchase_units": [ { "payments": { "authorizations": [
                  { "id": "AUTH-1", "status": "CREATED" }
                ] } } ] }
                """
            ),
            ["/v2/payments/authorizations/AUTH-1/void"] = (
                HttpStatusCode.InternalServerError,
                """{ "name": "INTERNAL_SERVER_ERROR" }"""
            ),
        };

        var result = await CancelAsync(handler, "ORDER-1");

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.CancelCharge.Failed", result.Error.Code);
    }

    private static Task<BuildingBlocks.Results.Result<WebhookEventPayload>> CancelAsync(
        UrlHandler handler,
        string reference
    ) =>
        new PayPalPaymentAdapter(
            new StubHttpClientFactory(handler),
            NullLogger<PayPalPaymentAdapter>.Instance
        ).CancelChargeAsync(Credentials, new ChargeStatusLookup(reference), CancellationToken.None);

    private static (HttpStatusCode, string) Ok(string body) => (HttpStatusCode.OK, body);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Responde por ruta; el token OAuth siempre sale bien. Anota "MÉTODO ruta".</summary>
    private sealed class UrlHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes = [];

        public List<string> Requested { get; } = [];

        public (HttpStatusCode Status, string Body) this[string path]
        {
            set => _routes[path] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requested.Add($"{request.Method.Method} {path}");
            var (status, body) =
                path == "/v1/oauth2/token" ? (HttpStatusCode.OK, """{ "access_token": "A21AA" }""")
                : _routes.TryGetValue(path, out var route) ? route
                : (HttpStatusCode.NotFound, "{}");
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
        }
    }
}
