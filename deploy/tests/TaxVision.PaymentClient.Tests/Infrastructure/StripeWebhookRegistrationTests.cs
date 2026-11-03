using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>Registro del webhook en Stripe (WebhookEndpointService) contra un stub HTTP: sin red.</summary>
public sealed class StripeWebhookRegistrationTests
{
    private static readonly TenantProviderCredentials Credentials = new("sk_test_office", null);
    private static readonly Uri Url = new("https://api.example.test/payments-client/webhooks/t1/stripe");

    [Fact]
    public async Task Creates_the_endpoint_with_the_events_and_the_SDK_api_version_and_returns_its_secret()
    {
        var stripe = new StripeStub
        {
            List = """{ "object": "list", "data": [], "has_more": false }""",
            Create = """{ "id": "we_new", "object": "webhook_endpoint", "secret": "whsec_new", "url": "x" }""",
        };

        var result = await Adapter(stripe)
            .RegisterWebhookAsync(Credentials, new(Url, ["charge.refunded", "charge.dispute.created"]), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new RegisteredWebhook("we_new", "whsec_new"), result.Value);
        var form = Uri.UnescapeDataString(stripe.CreateBody!);
        Assert.Contains($"url={Url.AbsoluteUri}", form);
        Assert.Contains("enabled_events[0]=charge.refunded", form);
        Assert.Contains("enabled_events[1]=charge.dispute.created", form);
        Assert.Contains($"api_version={StripeConfiguration.ApiVersion}", form);
        Assert.Equal("Bearer sk_test_office", stripe.Authorization);
    }

    [Fact]
    public async Task An_existing_endpoint_for_our_url_is_replaced_because_its_secret_cannot_be_read()
    {
        var stripe = new StripeStub
        {
            List = $$"""
                { "object": "list", "has_more": false, "data": [
                  { "id": "we_old", "object": "webhook_endpoint", "url": "{{Url.AbsoluteUri}}" },
                  { "id": "we_other", "object": "webhook_endpoint", "url": "https://elsewhere.test/hook" }
                ] }
                """,
            Create = """{ "id": "we_new", "object": "webhook_endpoint", "secret": "whsec_new" }""",
        };

        var result = await Adapter(stripe).RegisterWebhookAsync(Credentials, new(Url, ["charge.refunded"]), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(["we_old"], stripe.Deleted);
    }

    [Fact]
    public async Task A_key_without_permission_is_a_permanent_failure()
    {
        var stripe = new StripeStub
        {
            Status = HttpStatusCode.Forbidden,
            List =
                """{ "error": { "type": "invalid_request_error", "message": "The provided key does not have the required permissions." } }""",
        };

        var result = await Adapter(stripe).RegisterWebhookAsync(Credentials, new(Url, ["charge.refunded"]), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Stripe.Webhook.invalid_request_error", result.Error.Code);
    }

    [Fact]
    public async Task A_Stripe_server_error_is_transient()
    {
        var stripe = new StripeStub
        {
            Status = HttpStatusCode.InternalServerError,
            List = """{ "error": { "type": "api_error", "message": "boom" } }""",
        };

        var result = await Adapter(stripe).RegisterWebhookAsync(Credentials, new(Url, ["charge.refunded"]), default);

        Assert.Equal(WebhookRegistrationErrors.TransientCode, result.Error.Code);
    }

    [Fact]
    public async Task Deleting_a_missing_endpoint_counts_as_deleted()
    {
        var stripe = new StripeStub
        {
            DeleteStatus = HttpStatusCode.NotFound,
            DeleteBody =
                """{ "error": { "type": "invalid_request_error", "code": "resource_missing", "message": "No such webhook endpoint" } }""",
        };

        var result = await Adapter(stripe).DeleteWebhookAsync(Credentials, "we_gone", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(["we_gone"], stripe.Deleted);
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

    /// <summary>Responde como la API de webhook_endpoints: GET lista, POST crea, DELETE borra.</summary>
    private sealed class StripeStub : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string List { get; init; } = """{ "object": "list", "data": [], "has_more": false }""";
        public string Create { get; init; } = "{}";
        public HttpStatusCode DeleteStatus { get; init; } = HttpStatusCode.OK;
        public string? DeleteBody { get; init; }

        public string? CreateBody { get; private set; }
        public string? Authorization { get; private set; }
        public List<string> Deleted { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Authorization = request.Headers.Authorization?.ToString();
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Delete)
            {
                var id = path[(path.LastIndexOf('/') + 1)..];
                Deleted.Add(id);
                return Json(
                    DeleteStatus,
                    DeleteBody ?? $$"""{ "id": "{{id}}", "object": "webhook_endpoint", "deleted": true }"""
                );
            }

            if (request.Method == HttpMethod.Post)
            {
                CreateBody = await request.Content!.ReadAsStringAsync(ct);
                return Json(Status, Create);
            }

            return Json(Status, List);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
