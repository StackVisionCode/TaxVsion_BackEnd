using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Providers.PayPal;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>
/// Verifica la ruta de cobro de PayPal en PaymentClient: el frontend manda el orderId ya aprobado y el
/// adapter hace OAuth + capture. Un stub de HttpMessageHandler responde el token y la captura; así el
/// test no toca la red real. Mismo espíritu que PayPalPaymentAdapterTests de PaymentApp.
/// </summary>
public sealed class PayPalPaymentAdapterTests
{
    private static readonly TenantProviderCredentials Credentials = new(
        SecretKey: "secret",
        WebhookSecret: null,
        PublishableKey: "client-id",
        ApiBaseUrl: "https://api-m.sandbox.paypal.com"
    );

    private static ChargeAuthorizationRequest Request(long amountCents = 15000, string currency = "USD") =>
        new(
            PaymentMethod: new PaymentMethodToken("ORDER-123"),
            Amount: Money.Create(amountCents, currency).Value,
            IdempotencyKey: IdempotencyKey.Create("idem-1").Value,
            Descriptor: StatementDescriptor.Create("TAXVISION").Value,
            ReceiptEmail: null,
            Metadata: new Dictionary<string, string>()
        );

    [Fact]
    public async Task Capture_completed_maps_to_Succeeded_with_capture_id()
    {
        const string captureBody = """
            {
              "id": "ORDER-123",
              "status": "COMPLETED",
              "purchase_units": [
                { "payments": { "captures": [
                  { "id": "CAP-999", "status": "COMPLETED", "amount": { "value": "150.00", "currency_code": "USD" } }
                ] } }
              ]
            }
            """;
        var adapter = CreateAdapter(RouteHandler(captureResponse: Ok(captureBody)));

        var result = await adapter.AuthorizeChargeAsync(Credentials, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, result.Value.Status);
        Assert.Equal("CAP-999", result.Value.ProviderChargeReference);
    }

    [Fact]
    public async Task Capture_amount_mismatch_is_rejected_as_Failed()
    {
        const string captureBody = """
            {
              "id": "ORDER-123",
              "status": "COMPLETED",
              "purchase_units": [
                { "payments": { "captures": [
                  { "id": "CAP-999", "status": "COMPLETED", "amount": { "value": "1.00", "currency_code": "USD" } }
                ] } }
              ]
            }
            """;
        var adapter = CreateAdapter(RouteHandler(captureResponse: Ok(captureBody)));

        var result = await adapter.AuthorizeChargeAsync(
            Credentials,
            Request(amountCents: 15000),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, result.Value.Status);
        Assert.Equal("paypal_amount_mismatch", result.Value.FailureCode);
    }

    [Fact]
    public async Task Capture_http_error_maps_to_Failed()
    {
        const string errorBody = """{ "name": "UNPROCESSABLE_ENTITY", "message": "Order already captured." }""";
        var adapter = CreateAdapter(
            RouteHandler(
                captureResponse: new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                {
                    Content = new StringContent(errorBody, Encoding.UTF8, "application/json"),
                }
            )
        );

        var result = await adapter.AuthorizeChargeAsync(Credentials, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, result.Value.Status);
        Assert.Equal("UNPROCESSABLE_ENTITY", result.Value.FailureCode);
    }

    [Fact]
    public async Task Missing_client_id_fails_fast_without_calling_paypal()
    {
        var adapter = CreateAdapter(new ThrowingHandler());
        var credsNoClient = Credentials with { PublishableKey = null };

        var result = await adapter.AuthorizeChargeAsync(credsNoClient, Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.MissingClientId", result.Error.Code);
    }

    // ---- helpers ----

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static RoutingHandler RouteHandler(HttpResponseMessage captureResponse)
    {
        const string tokenBody = """{ "access_token": "A21AA", "token_type": "Bearer", "expires_in": 32400 }""";
        return new RoutingHandler(Ok(tokenBody), captureResponse);
    }

    private static PayPalPaymentAdapter CreateAdapter(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), NullLogger<PayPalPaymentAdapter>.Instance);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Enruta por URL: /v1/oauth2/token → token; cualquier otra (capture) → captureResponse.</summary>
    private sealed class RoutingHandler(HttpResponseMessage tokenResponse, HttpResponseMessage otherResponse)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var response = path.EndsWith("/v1/oauth2/token", StringComparison.Ordinal) ? tokenResponse : otherResponse;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("PayPal should not be called when the client-id is missing.");
    }
}
