using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Infrastructure.Providers.PayPal;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>Consulta del estado de un cobro PayPal (GET de la captura v2, o de la orden si la referencia
/// es una orden) contra un stub HTTP: sin red.</summary>
public sealed class PayPalChargeStatusTests
{
    private const string Base = "https://api-m.sandbox.paypal.com";

    private static readonly TenantProviderCredentials Credentials = new(
        SecretKey: "secret",
        WebhookSecret: null,
        PublishableKey: "client-id",
        ApiBaseUrl: Base
    );

    [Fact]
    public async Task A_completed_capture_is_Succeeded_with_its_amount()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/CAP-1"] = Json(
                """{ "id": "CAP-1", "status": "COMPLETED", "amount": { "value": "25.00", "currency_code": "USD" } }"""
            ),
        };

        var result = await LookupAsync(handler, "CAP-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, result.Value.Status);
        Assert.Equal("CAP-1", result.Value.ProviderChargeReference);
        Assert.Equal(2500, result.Value.PaidAmountCents);
        Assert.Equal("USD", result.Value.PaidCurrency);
        Assert.Equal(["/v1/oauth2/token", "/v2/payments/captures/CAP-1"], handler.Requested);
    }

    [Fact]
    public async Task A_pending_capture_stays_Processing()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/CAP-1"] = Json(
                """{ "id": "CAP-1", "status": "PENDING", "status_details": { "reason": "PENDING_REVIEW" } }"""
            ),
        };

        var result = await LookupAsync(handler, "CAP-1");

        Assert.Equal(PaymentStatus.Processing, result.Value.Status);
    }

    [Theory]
    [InlineData("DECLINED")]
    [InlineData("FAILED")]
    public async Task A_declined_or_failed_capture_is_Failed(string status)
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/CAP-1"] = Json($$"""{ "id": "CAP-1", "status": "{{status}}" }"""),
        };

        var result = await LookupAsync(handler, "CAP-1");

        Assert.Equal(PaymentStatus.Failed, result.Value.Status);
        Assert.Equal(status, result.Value.FailureCode);
    }

    [Fact]
    public async Task A_capture_refunded_before_confirmation_is_not_applied()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/CAP-1"] = Json("""{ "id": "CAP-1", "status": "REFUNDED" }"""),
        };

        var result = await LookupAsync(handler, "CAP-1");

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.ChargeStatus.RefundedBeforeConfirmation", result.Error.Code);
    }

    [Fact]
    public async Task An_order_reference_falls_back_to_the_order_capture()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/ORDER-1"] = new(HttpStatusCode.NotFound, """{ "name": "RESOURCE_NOT_FOUND" }"""),
            ["/v2/checkout/orders/ORDER-1"] = Json(
                """
                { "id": "ORDER-1", "status": "COMPLETED", "purchase_units": [ { "payments": { "captures": [
                  { "id": "CAP-9", "status": "COMPLETED", "amount": { "value": "25.00", "currency_code": "USD" } }
                ] } } ] }
                """
            ),
        };

        var result = await LookupAsync(handler, "ORDER-1");

        Assert.Equal(PaymentStatus.Succeeded, result.Value.Status);
        Assert.Equal("ORDER-1", result.Value.ProviderChargeReference);
        Assert.Equal(2500, result.Value.PaidAmountCents);
    }

    [Fact]
    public async Task An_order_without_capture_stays_pending()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/ORDER-1"] = new(HttpStatusCode.NotFound, "{}"),
            ["/v2/checkout/orders/ORDER-1"] = Json("""{ "id": "ORDER-1", "status": "APPROVED" }"""),
        };

        var result = await LookupAsync(handler, "ORDER-1");

        Assert.Equal(PaymentStatus.Processing, result.Value.Status);
    }

    [Fact]
    public async Task A_PayPal_server_error_is_a_failure_to_retry_later()
    {
        var handler = new UrlHandler
        {
            ["/v2/payments/captures/CAP-1"] = new(
                HttpStatusCode.InternalServerError,
                """{ "name": "INTERNAL_SERVER_ERROR" }"""
            ),
        };

        var result = await LookupAsync(handler, "CAP-1");

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.ChargeStatus.Failed", result.Error.Code);
    }

    private static Task<BuildingBlocks.Results.Result<WebhookEventPayload>> LookupAsync(
        UrlHandler handler,
        string reference
    ) =>
        new PayPalPaymentAdapter(
            new StubHttpClientFactory(handler),
            NullLogger<PayPalPaymentAdapter>.Instance
        ).GetChargeStatusAsync(Credentials, new ChargeStatusLookup(reference), CancellationToken.None);

    private static (HttpStatusCode, string) Json(string body) => (HttpStatusCode.OK, body);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Responde por ruta; el token OAuth siempre sale bien. Anota las rutas pedidas.</summary>
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
            Requested.Add(path);
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
