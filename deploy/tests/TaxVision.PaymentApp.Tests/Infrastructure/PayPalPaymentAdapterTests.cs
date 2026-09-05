using System.Net;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Caching;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

namespace TaxVision.PaymentApp.Tests.Infrastructure;

public sealed class PayPalPaymentAdapterTests
{
    [Fact]
    public async Task Hosted_checkout_creates_paypal_order_with_oauth_and_idempotency()
    {
        var handler = new CapturingHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """
            {"access_token":"token_123","token_type":"Bearer","expires_in":3600}
            """
        );
        handler.EnqueueJson(
            HttpStatusCode.Created,
            """
            {
              "id": "ORDER-123",
              "status": "PAYER_ACTION_REQUIRED",
              "links": [
                {
                  "href": "https://www.sandbox.paypal.com/checkoutnow?token=ORDER-123",
                  "rel": "payer-action",
                  "method": "GET"
                }
              ]
            }
            """
        );

        var adapter = CreateAdapter(handler);
        var request = CreateHostedRequest(PaymentMethodKind.Wallet);

        var result = await adapter.CreateHostedCheckoutSessionAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ORDER-123", result.Value.ProviderSessionId);
        Assert.Equal("ORDER-123", result.Value.ProviderPaymentIntentReference);
        Assert.Equal("https://www.sandbox.paypal.com/checkoutnow?token=ORDER-123", result.Value.CheckoutUrl);

        Assert.Equal(2, handler.Requests.Count);
        var oauth = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, oauth.Method);
        Assert.Equal("/v1/oauth2/token", oauth.Path);
        Assert.Equal("Basic", oauth.AuthorizationScheme);
        Assert.Contains("grant_type=client_credentials", oauth.Body, StringComparison.Ordinal);

        var order = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, order.Method);
        Assert.Equal("/v2/checkout/orders", order.Path);
        Assert.Equal("Bearer", order.AuthorizationScheme);
        Assert.Equal("token_123", order.AuthorizationParameter);
        Assert.Equal("onboarding-paypal-key", order.Headers["PayPal-Request-Id"]);
        Assert.Equal("return=representation", order.Headers["Prefer"]);

        using var document = JsonDocument.Parse(order.Body);
        var root = document.RootElement;
        Assert.Equal("CAPTURE", root.GetProperty("intent").GetString());

        var experience = root.GetProperty("payment_source").GetProperty("paypal").GetProperty("experience_context");
        Assert.Equal("IMMEDIATE_PAYMENT_REQUIRED", experience.GetProperty("payment_method_preference").GetString());
        Assert.Equal("NO_SHIPPING", experience.GetProperty("shipping_preference").GetString());
        Assert.Equal("PAY_NOW", experience.GetProperty("user_action").GetString());
        Assert.Equal("https://app.example.com/success", experience.GetProperty("return_url").GetString());
        Assert.Equal("https://app.example.com/cancel", experience.GetProperty("cancel_url").GetString());

        var purchaseUnit = root.GetProperty("purchase_units")[0];
        Assert.Equal("11111111111111111111111111111111", purchaseUnit.GetProperty("reference_id").GetString());
        Assert.Equal("11111111111111111111111111111111", purchaseUnit.GetProperty("custom_id").GetString());
        Assert.Equal("TAXVISION SAAS", purchaseUnit.GetProperty("description").GetString());
        Assert.Equal("USD", purchaseUnit.GetProperty("amount").GetProperty("currency_code").GetString());
        Assert.Equal("49.00", purchaseUnit.GetProperty("amount").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Hosted_checkout_rejects_non_wallet_method_before_calling_paypal()
    {
        var handler = new CapturingHttpMessageHandler();
        var adapter = CreateAdapter(handler);

        var result = await adapter.CreateHostedCheckoutSessionAsync(
            CreateHostedRequest(PaymentMethodKind.Card),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.CheckoutSession.MethodUnsupported", result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Hosted_checkout_reuses_cached_oauth_token_for_multiple_orders()
    {
        var handler = new CapturingHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"access_token":"token_123","token_type":"Bearer","expires_in":3600}"""
        );
        handler.EnqueueJson(
            HttpStatusCode.Created,
            """{"id":"ORDER-1","status":"PAYER_ACTION_REQUIRED","links":[{"href":"https://paypal.test/1","rel":"payer-action","method":"GET"}]}"""
        );
        handler.EnqueueJson(
            HttpStatusCode.Created,
            """{"id":"ORDER-2","status":"PAYER_ACTION_REQUIRED","links":[{"href":"https://paypal.test/2","rel":"payer-action","method":"GET"}]}"""
        );

        var adapter = CreateAdapter(handler);
        var first = await adapter.CreateHostedCheckoutSessionAsync(
            CreateHostedRequest(PaymentMethodKind.Wallet),
            CancellationToken.None
        );
        var second = await adapter.CreateHostedCheckoutSessionAsync(
            CreateHostedRequest(PaymentMethodKind.Wallet),
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, handler.Requests.Count(x => x.Path == "/v1/oauth2/token"));
        Assert.Equal(2, handler.Requests.Count(x => x.Path == "/v2/checkout/orders"));
    }

    [Fact]
    public async Task Gateway_fails_before_http_when_credentials_are_missing()
    {
        var handler = new CapturingHttpMessageHandler();
        var gateway = CreateGateway(handler, new PayPalOptions { ClientId = "", ClientSecret = "" });

        var result = await gateway.CreateOrderAsync(
            new PayPalCreateOrderRequest(
                "49.00",
                "USD",
                "TAXVISION SAAS",
                "https://app.example.com/success",
                "https://app.example.com/cancel",
                "key",
                new Dictionary<string, string>()
            ),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.ConfigurationMissing", result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Refund_posts_capture_refund_with_oauth_idempotency_and_amount()
    {
        var handler = new CapturingHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"access_token":"token_123","token_type":"Bearer","expires_in":3600}"""
        );
        handler.EnqueueJson(HttpStatusCode.Created, """{"id":"REFUND-123","status":"COMPLETED"}""");
        var adapter = CreateAdapter(handler);

        var result = await adapter.RefundAsync(
            "CAPTURE-123",
            Money.Create(4900, "USD").Value,
            "Admin approved onboarding refund.",
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("REFUND-123", result.Value.ProviderRefundReference);
        Assert.Equal(PaymentStatus.Refunded, result.Value.Status);
        Assert.Equal(4900, result.Value.RefundedAmount.AmountCents);

        Assert.Equal(2, handler.Requests.Count);
        var refund = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, refund.Method);
        Assert.Equal("/v2/payments/captures/CAPTURE-123/refund", refund.Path);
        Assert.Equal("Bearer", refund.AuthorizationScheme);
        Assert.Equal("token_123", refund.AuthorizationParameter);
        Assert.Equal("paypal-refund-CAPTURE-123-4900-USD", refund.Headers["PayPal-Request-Id"]);
        Assert.Equal("return=representation", refund.Headers["Prefer"]);

        using var document = JsonDocument.Parse(refund.Body);
        var amount = document.RootElement.GetProperty("amount");
        Assert.Equal("USD", amount.GetProperty("currency_code").GetString());
        Assert.Equal("49.00", amount.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Webhook_signature_verification_uses_paypal_verify_api_and_returns_event_identity()
    {
        var handler = new CapturingHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"access_token":"token_123","token_type":"Bearer","expires_in":3600}"""
        );
        handler.EnqueueJson(HttpStatusCode.OK, """{"verification_status":"SUCCESS"}""");
        var adapter = CreateAdapter(handler);
        var payload = """
            {
              "id": "WH-EVENT-1",
              "event_type": "PAYMENT.CAPTURE.COMPLETED",
              "resource": { "id": "CAPTURE-123" }
            }
            """;

        var result = await adapter.VerifyWebhookSignatureAsync(
            new ProviderWebhookVerificationRequest(payload, PayPalHeaders(), SigningSecret: null, WebhookId: "WH-123"),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("WH-EVENT-1", result.Value.ProviderEventId);
        Assert.Equal("PAYMENT.CAPTURE.COMPLETED", result.Value.EventType);

        Assert.Equal(2, handler.Requests.Count);
        var verificationRequest = handler.Requests[1];
        Assert.Equal("/v1/notifications/verify-webhook-signature", verificationRequest.Path);
        Assert.Equal("Bearer", verificationRequest.AuthorizationScheme);

        using var document = JsonDocument.Parse(verificationRequest.Body);
        var root = document.RootElement;
        Assert.Equal("SHA256withRSA", root.GetProperty("auth_algo").GetString());
        Assert.Equal("https://api-m.sandbox.paypal.com/certs/test", root.GetProperty("cert_url").GetString());
        Assert.Equal("transmission-id", root.GetProperty("transmission_id").GetString());
        Assert.Equal("signature", root.GetProperty("transmission_sig").GetString());
        Assert.Equal("2026-09-04T12:00:00Z", root.GetProperty("transmission_time").GetString());
        Assert.Equal("WH-123", root.GetProperty("webhook_id").GetString());
        Assert.Equal("WH-EVENT-1", root.GetProperty("webhook_event").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Webhook_signature_verification_rejects_failed_paypal_verification()
    {
        var handler = new CapturingHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"access_token":"token_123","token_type":"Bearer","expires_in":3600}"""
        );
        handler.EnqueueJson(HttpStatusCode.OK, """{"verification_status":"FAILURE"}""");
        var adapter = CreateAdapter(handler);

        var result = await adapter.VerifyWebhookSignatureAsync(
            new ProviderWebhookVerificationRequest(
                """{"id":"WH-EVENT-1","event_type":"PAYMENT.CAPTURE.COMPLETED","resource":{"id":"CAPTURE-123"}}""",
                PayPalHeaders(),
                SigningSecret: null,
                WebhookId: "WH-123"
            ),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.WebhookSignature.Invalid", result.Error.Code);
    }

    [Fact]
    public async Task Parse_webhook_maps_paypal_capture_completed_to_succeeded_with_capture_reconciliation()
    {
        var adapter = CreateAdapter(new CapturingHttpMessageHandler());
        var payload = """
            {
              "id": "WH-EVENT-1",
              "event_type": "PAYMENT.CAPTURE.COMPLETED",
              "resource": {
                "id": "CAPTURE-123",
                "supplementary_data": {
                  "related_ids": { "order_id": "ORDER-123" }
                }
              }
            }
            """;

        var result = await adapter.ParseWebhookEventAsync(payload, "PAYMENT.CAPTURE.COMPLETED", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ORDER-123", result.Value.ProviderChargeReference);
        Assert.Equal(PaymentStatus.Succeeded, result.Value.Status);
        Assert.Equal("CAPTURE-123", result.Value.ReconciledChargeReference);
    }

    [Fact]
    public async Task Parse_webhook_maps_paypal_order_approved_to_processing_only()
    {
        var adapter = CreateAdapter(new CapturingHttpMessageHandler());

        var result = await adapter.ParseWebhookEventAsync(
            """{"id":"WH-EVENT-2","event_type":"CHECKOUT.ORDER.APPROVED","resource":{"id":"ORDER-123"}}""",
            "CHECKOUT.ORDER.APPROVED",
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("ORDER-123", result.Value.ProviderChargeReference);
        Assert.Equal(PaymentStatus.Processing, result.Value.Status);
        Assert.Null(result.Value.ReconciledChargeReference);
    }

    [Fact]
    public async Task Get_charge_status_keeps_approved_order_processing_without_capture()
    {
        var handler = new CapturingHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"access_token":"token_123","token_type":"Bearer","expires_in":3600}"""
        );
        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"ORDER-123","status":"APPROVED"}""");
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetChargeStatusAsync("ORDER-123", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, result.Value.Status);
        Assert.Equal("ORDER-123", result.Value.ProviderChargeReference);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Finalize_hosted_checkout_captures_approved_order_and_returns_succeeded_capture_reference()
    {
        var handler = new CapturingHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"access_token":"token_123","token_type":"Bearer","expires_in":3600}"""
        );
        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"ORDER-123","status":"APPROVED"}""");
        handler.EnqueueJson(
            HttpStatusCode.Created,
            """
            {
              "id": "ORDER-123",
              "status": "COMPLETED",
              "purchase_units": [
                {
                  "payments": {
                    "captures": [
                      { "id": "CAPTURE-123", "status": "COMPLETED" }
                    ]
                  }
                }
              ]
            }
            """
        );
        var adapter = CreateAdapter(handler);

        var result = await adapter.FinalizeHostedCheckoutAsync(
            "ORDER-123",
            Money.Create(4900, "USD").Value,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, result.Value.Status);
        Assert.Equal("CAPTURE-123", result.Value.ProviderChargeReference);
        Assert.Equal("/v2/checkout/orders/ORDER-123", handler.Requests[1].Path);
        Assert.Equal("/v2/checkout/orders/ORDER-123/capture", handler.Requests[2].Path);
        Assert.Equal("paypal-capture-ORDER-123", handler.Requests[2].Headers["PayPal-Request-Id"]);
    }

    [Fact]
    public async Task Get_charge_status_keeps_payer_action_required_as_processing_without_capture()
    {
        var handler = new CapturingHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"access_token":"token_123","token_type":"Bearer","expires_in":3600}"""
        );
        handler.EnqueueJson(HttpStatusCode.OK, """{"id":"ORDER-123","status":"PAYER_ACTION_REQUIRED"}""");
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetChargeStatusAsync("ORDER-123", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, result.Value.Status);
        Assert.Equal("ORDER-123", result.Value.ProviderChargeReference);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Get_charge_status_maps_denied_capture_to_failed_without_reconciling_reference()
    {
        var handler = new CapturingHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """{"access_token":"token_123","token_type":"Bearer","expires_in":3600}"""
        );
        handler.EnqueueJson(
            HttpStatusCode.OK,
            """
            {
              "id": "ORDER-123",
              "status": "COMPLETED",
              "purchase_units": [
                {
                  "payments": {
                    "captures": [
                      {
                        "id": "CAPTURE-123",
                        "status": "DENIED",
                        "status_details": { "reason": "INSTRUMENT_DECLINED" }
                      }
                    ]
                  }
                }
              ]
            }
            """
        );
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetChargeStatusAsync("ORDER-123", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, result.Value.Status);
        Assert.Equal("ORDER-123", result.Value.ProviderChargeReference);
        Assert.Equal("PayPal.DENIED", result.Value.FailureCode);
        Assert.Equal("INSTRUMENT_DECLINED", result.Value.FailureMessage);
    }

    private static PayPalPaymentAdapter CreateAdapter(CapturingHttpMessageHandler handler)
    {
        var gateway = CreateGateway(
            handler,
            new PayPalOptions
            {
                ClientId = "paypal-client-id",
                ClientSecret = "paypal-client-secret",
                BaseUrl = "https://api-m.sandbox.paypal.com",
            }
        );

        return new PayPalPaymentAdapter(gateway, NullLogger<PayPalPaymentAdapter>.Instance);
    }

    private static PayPalGateway CreateGateway(CapturingHttpMessageHandler handler, PayPalOptions options)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/"),
        };

        return new PayPalGateway(
            http,
            Options.Create(options),
            new InMemoryCacheService(),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-09-04T12:00:00Z")),
            NullLogger<PayPalGateway>.Instance
        );
    }

    private static IReadOnlyDictionary<string, string> PayPalHeaders() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PAYPAL-AUTH-ALGO"] = "SHA256withRSA",
            ["PAYPAL-CERT-URL"] = "https://api-m.sandbox.paypal.com/certs/test",
            ["PAYPAL-TRANSMISSION-ID"] = "transmission-id",
            ["PAYPAL-TRANSMISSION-SIG"] = "signature",
            ["PAYPAL-TRANSMISSION-TIME"] = "2026-09-04T12:00:00Z",
        };

    private static HostedCheckoutSessionRequest CreateHostedRequest(PaymentMethodKind method) =>
        new(
            Amount: Money.Create(4900, "USD").Value,
            Method: method,
            IdempotencyKey: IdempotencyKey.Create("onboarding-paypal-key").Value,
            Descriptor: StatementDescriptor.Create("TAXVISION SAAS").Value,
            PayerEmail: "buyer@example.com",
            SuccessUrl: "https://app.example.com/success",
            CancelUrl: "https://app.example.com/cancel",
            ExpiresAtUtc: DateTime.Parse("2026-09-05T12:00:00Z"),
            Metadata: new Dictionary<string, string>
            {
                ["onboardingId"] = "11111111111111111111111111111111",
                ["saaSPaymentId"] = "22222222222222222222222222222222",
            }
        );

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
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

            Requests.Add(
                new CapturedRequest(
                    request.Method,
                    request.RequestUri?.AbsolutePath ?? string.Empty,
                    request.Headers.Authorization?.Scheme,
                    request.Headers.Authorization?.Parameter,
                    request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value)),
                    body
                )
            );

            if (_responses.Count == 0)
                throw new InvalidOperationException("No fake HTTP response was queued.");

            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        IReadOnlyDictionary<string, string> Headers,
        string Body
    );

    private sealed class InMemoryCacheService : ICacheService
    {
        private readonly Dictionary<string, object?> _values = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            return Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : default);
        }

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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
