using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Infrastructure.Providers.PayPal;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>Registro del webhook en PayPal (Webhooks v1) contra un stub HTTP: sin red.</summary>
public sealed class PayPalWebhookRegistrationTests
{
    private static readonly TenantProviderCredentials Credentials = new(
        SecretKey: "secret",
        WebhookSecret: null,
        PublishableKey: "client-id",
        ApiBaseUrl: "https://api-m.sandbox.paypal.com"
    );

    private static readonly Uri Url = new("https://api.example.test/payments-client/webhooks/t1/paypal");

    [Fact]
    public async Task Creates_the_webhook_and_its_id_is_the_secret_to_store()
    {
        var paypal = new PayPalStub { Create = (HttpStatusCode.Created, """{ "id": "WH-1", "url": "x" }""") };

        var result = await Adapter(paypal)
            .RegisterWebhookAsync(
                Credentials,
                new(Url, ["PAYMENT.CAPTURE.REFUNDED", "CUSTOMER.DISPUTE.CREATED"]),
                default
            );

        Assert.Equal(new RegisteredWebhook("WH-1", "WH-1"), result.Value);
        using var body = JsonDocument.Parse(paypal.CreateBody!);
        Assert.Equal(Url.AbsoluteUri, body.RootElement.GetProperty("url").GetString());
        Assert.Equal(
            ["PAYMENT.CAPTURE.REFUNDED", "CUSTOMER.DISPUTE.CREATED"],
            body.RootElement.GetProperty("event_types").EnumerateArray().Select(e => e.GetProperty("name").GetString())
        );
    }

    [Fact]
    public async Task A_url_already_registered_reuses_the_existing_webhook()
    {
        var paypal = new PayPalStub
        {
            Create = (
                HttpStatusCode.BadRequest,
                """{ "name": "WEBHOOK_URL_ALREADY_EXISTS", "message": "Webhook URL already exists." }"""
            ),
            List = $$"""
                { "webhooks": [
                  { "id": "WH-OTHER", "url": "https://elsewhere.test/hook" },
                  { "id": "WH-EXISTING", "url": "{{Url.AbsoluteUri}}" }
                ] }
                """,
        };

        var result = await Adapter(paypal)
            .RegisterWebhookAsync(Credentials, new(Url, ["PAYMENT.CAPTURE.REFUNDED"]), default);

        Assert.Equal(new RegisteredWebhook("WH-EXISTING", "WH-EXISTING"), result.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    public async Task Server_errors_are_transient_and_rejections_are_not(HttpStatusCode status, bool transient)
    {
        var paypal = new PayPalStub { Create = (status, """{ "name": "SOMETHING", "message": "no" }""") };

        var result = await Adapter(paypal)
            .RegisterWebhookAsync(Credentials, new(Url, ["PAYMENT.CAPTURE.REFUNDED"]), default);

        Assert.True(result.IsFailure);
        Assert.Equal(transient, result.Error.Code == WebhookRegistrationErrors.TransientCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Deleting_succeeds_also_when_the_webhook_no_longer_exists(HttpStatusCode status)
    {
        var paypal = new PayPalStub { DeleteStatus = status };

        var result = await Adapter(paypal).DeleteWebhookAsync(Credentials, "WH-1", default);

        Assert.True(result.IsSuccess);
        Assert.Equal("/v1/notifications/webhooks/WH-1", paypal.DeletedPath);
    }

    private static PayPalPaymentAdapter Adapter(PayPalStub stub) =>
        new(new StubHttpClientFactory(stub), NullLogger<PayPalPaymentAdapter>.Instance);

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class PayPalStub : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body) Create { get; init; } = (HttpStatusCode.Created, "{}");
        public string List { get; init; } = """{ "webhooks": [] }""";
        public HttpStatusCode DeleteStatus { get; init; } = HttpStatusCode.NoContent;

        public string? CreateBody { get; private set; }
        public string? DeletedPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/oauth2/token")
                return Json(HttpStatusCode.OK, """{ "access_token": "A21AA" }""");

            if (request.Method == HttpMethod.Delete)
            {
                DeletedPath = path;
                return new HttpResponseMessage(DeleteStatus) { Content = new StringContent(string.Empty) };
            }

            if (request.Method == HttpMethod.Post)
            {
                CreateBody = await request.Content!.ReadAsStringAsync(ct);
                return Json(Create.Status, Create.Body);
            }

            return Json(HttpStatusCode.OK, List);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
