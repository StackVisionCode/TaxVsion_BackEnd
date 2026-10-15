using System.Net;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

namespace TaxVision.PaymentApp.Tests.Infrastructure.PayPal;

public sealed class PayPalOffSessionChargerTests
{
    [Fact]
    public async Task Succeeded_order_already_completed_returns_capture_reference()
    {
        var handler = new CapturingHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"access_token":"t","token_type":"Bearer","expires_in":3600}""");
        handler.EnqueueJson(
            HttpStatusCode.Created,
            """
            {
              "id": "ORDER-OFF-1",
              "status": "COMPLETED",
              "purchase_units": [
                { "payments": { "captures": [ { "id": "CAPTURE-OFF-1", "status": "COMPLETED" } ] } }
              ]
            }
            """
        );

        var charger = CreateCharger(handler);
        var result = await charger.ChargeAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OffSessionChargeStatus.Succeeded, result.Value.Status);
        Assert.Equal("CAPTURE-OFF-1", result.Value.ProviderPaymentReference);
        var createCall = handler.Requests.Single(r => r.Path == "/v2/checkout/orders");
        using var doc = JsonDocument.Parse(createCall.Body);
        var token = doc.RootElement.GetProperty("payment_source").GetProperty("token");
        Assert.Equal("vault-token-abc", token.GetProperty("id").GetString());
        Assert.Equal("PAYMENT_METHOD_TOKEN", token.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Payer_action_required_maps_to_requires_user_action()
    {
        var handler = new CapturingHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"access_token":"t","token_type":"Bearer","expires_in":3600}""");
        handler.EnqueueJson(HttpStatusCode.Created, """{"id":"ORDER-OFF-2","status":"PAYER_ACTION_REQUIRED"}""");

        var charger = CreateCharger(handler);
        var result = await charger.ChargeAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OffSessionChargeStatus.RequiresUserAction, result.Value.Status);
        Assert.Equal("ORDER-OFF-2", result.Value.ProviderPaymentReference);
        Assert.Equal("PayPal.PayerActionRequired", result.Value.FailureCode);
    }

    [Fact]
    public async Task Http_failure_maps_to_failed_outcome_without_throwing()
    {
        var handler = new CapturingHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """{"access_token":"t","token_type":"Bearer","expires_in":3600}""");
        handler.EnqueueJson(HttpStatusCode.UnprocessableEntity, """{"name":"UNPROCESSABLE_ENTITY"}""");

        var charger = CreateCharger(handler);
        var result = await charger.ChargeAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OffSessionChargeStatus.Failed, result.Value.Status);
        Assert.Equal("PayPal.OffSession.CreateFailed", result.Value.FailureCode);
    }

    [Fact]
    public async Task Missing_token_fails_before_calling_paypal()
    {
        var handler = new CapturingHandler();
        var charger = CreateCharger(handler);

        var result = await charger.ChargeAsync(BuildRequest(providerToken: ""), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OffSessionChargeStatus.Failed, result.Value.Status);
        Assert.Equal("PayPal.OffSession.MissingToken", result.Value.FailureCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Provider_code_matches_registered_key()
    {
        var charger = CreateCharger(new CapturingHandler());
        Assert.Equal("PayPal", charger.ProviderCode);
    }

    private static OffSessionChargeRequest BuildRequest(string providerToken = "vault-token-abc") =>
        new(
            TenantId: Guid.NewGuid(),
            SaaSPaymentId: Guid.NewGuid(),
            ProviderCode: "PayPal",
            ProviderToken: providerToken,
            AmountCents: 4900,
            Currency: "USD",
            IdempotencyKey: "renewal-charge-1",
            PayerEmail: "payer@example.com"
        );

    private static PayPalOffSessionCharger CreateCharger(CapturingHandler handler)
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
            new InMemoryCache(),
            TimeProvider.System,
            NullLogger<PayPalGateway>.Instance
        );
        return new PayPalOffSessionCharger(gateway, NullLogger<PayPalOffSessionCharger>.Instance);
    }

    internal sealed class CapturingHandler : HttpMessageHandler
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

    private sealed class InMemoryCache : ICacheService
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
