using System.Net;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

namespace TaxVision.PaymentApp.Tests.Infrastructure.PayPal;

public sealed class PayPalHostedCheckoutSessionCreatorTests
{
    [Fact]
    public async Task Store_method_for_offsession_requests_vault_on_success_and_merchant_customer_id()
    {
        var handler = EnqueueOrderCreated();
        var creator = CreateCreator(handler);
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var paymentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var result = await creator.CreateAsync(
            new HostedCheckoutCreateRequest(
                TenantId: tenantId,
                SaaSPaymentId: paymentId,
                AmountCents: 4900,
                Currency: "USD",
                PayerEmail: "payer@example.com",
                SuccessUrl: "https://app.example.com/success",
                CancelUrl: "https://app.example.com/cancel",
                IdempotencyKey: "renewal-key-1",
                Provider: "PayPal",
                Method: "Wallet",
                StoreMethodForOffSession: true
            ),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(paymentId, result.Value.PaymentId);
        Assert.Equal("https://paypal.test/order-approve", result.Value.CheckoutUrl);

        var order = handler.Requests.Single(r => r.Path == "/v2/checkout/orders");
        using var doc = JsonDocument.Parse(order.Body);
        var attributes = doc.RootElement.GetProperty("payment_source").GetProperty("paypal").GetProperty("attributes");
        Assert.Equal("ON_SUCCESS", attributes.GetProperty("vault").GetProperty("store_in_vault").GetString());
        Assert.Equal("MERCHANT", attributes.GetProperty("vault").GetProperty("usage_type").GetString());
        var expectedCustomerId = PayPalHostedCheckoutSessionCreator.BuildMerchantCustomerId(tenantId);
        Assert.Equal(expectedCustomerId, attributes.GetProperty("customer").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Non_store_method_request_omits_vault_block()
    {
        var handler = EnqueueOrderCreated();
        var creator = CreateCreator(handler);

        var result = await creator.CreateAsync(
            new HostedCheckoutCreateRequest(
                TenantId: Guid.NewGuid(),
                SaaSPaymentId: Guid.NewGuid(),
                AmountCents: 4900,
                Currency: "USD",
                PayerEmail: "payer@example.com",
                SuccessUrl: "https://app.example.com/success",
                CancelUrl: "https://app.example.com/cancel",
                IdempotencyKey: "renewal-key-2",
                Provider: "PayPal",
                Method: "Wallet",
                StoreMethodForOffSession: false
            ),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var order = handler.Requests.Single(r => r.Path == "/v2/checkout/orders");
        using var doc = JsonDocument.Parse(order.Body);
        var paypal = doc.RootElement.GetProperty("payment_source").GetProperty("paypal");
        Assert.False(paypal.TryGetProperty("attributes", out _));
    }

    [Fact]
    public async Task Missing_tenant_fails_before_calling_paypal()
    {
        var handler = new CapturingHttpMessageHandler();
        var creator = CreateCreator(handler);

        var result = await creator.CreateAsync(
            new HostedCheckoutCreateRequest(
                TenantId: Guid.Empty,
                SaaSPaymentId: Guid.NewGuid(),
                AmountCents: 4900,
                Currency: "USD",
                PayerEmail: "payer@example.com",
                SuccessUrl: "https://app.example.com/success",
                CancelUrl: "https://app.example.com/cancel",
                IdempotencyKey: "renewal-key-3",
                Provider: "PayPal",
                Method: "Wallet",
                StoreMethodForOffSession: true
            ),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.HostedCheckout.MissingTenant", result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    private static CapturingHttpMessageHandler EnqueueOrderCreated()
    {
        var handler = new CapturingHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"access_token":"token_123","token_type":"Bearer","expires_in":3600}"""
        );
        handler.EnqueueJson(
            HttpStatusCode.Created,
            """
            {
              "id": "ORDER-999",
              "status": "PAYER_ACTION_REQUIRED",
              "links": [
                { "href": "https://paypal.test/order-approve", "rel": "payer-action", "method": "GET" }
              ]
            }
            """
        );
        return handler;
    }

    private static PayPalHostedCheckoutSessionCreator CreateCreator(CapturingHttpMessageHandler handler)
    {
        var options = new PayPalOptions
        {
            ClientId = "paypal-client-id",
            ClientSecret = "paypal-client-secret",
            BaseUrl = "https://api-m.sandbox.paypal.com",
        };

        var http = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl + "/") };
        var gateway = new PayPalGateway(
            http,
            Options.Create(options),
            new InMemoryCacheService(),
            TimeProvider.System,
            NullLogger<PayPalGateway>.Instance
        );
        return new PayPalHostedCheckoutSessionCreator(gateway, NullLogger<PayPalHostedCheckoutSessionCreator>.Instance);
    }

    internal sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<CapturedRequest> Requests { get; } = [];

        public void EnqueueJson(HttpStatusCode statusCode, string json)
        {
            _responses.Enqueue(
                new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri?.AbsolutePath ?? string.Empty, body));
            if (_responses.Count == 0)
                throw new InvalidOperationException("No fake HTTP response was queued.");
            return _responses.Dequeue();
        }
    }

    internal sealed record CapturedRequest(HttpMethod Method, string Path, string Body);

    private sealed class InMemoryCacheService : ICacheService
    {
        private readonly Dictionary<string, object?> _values = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        )
        {
            if (_values.TryGetValue(key, out var value))
                return (T)value!;
            var created = await factory(ct);
            _values[key] = created;
            return created;
        }
    }
}
