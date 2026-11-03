using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Infrastructure.Providers.PayPal;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>
/// Webhooks de PayPal: la verificación por postback (<c>/v1/notifications/verify-webhook-signature</c>) con
/// HTTP falso — válida, inválida, PayPal caído y encabezados faltantes — y el parseo de cada evento que
/// procesa el adaptador a los mismos datos canónicos que Stripe.
/// </summary>
public sealed class PayPalWebhookTests
{
    private const string WebhookId = "8PT597110X687430LKGECATA";

    private static readonly TenantProviderCredentials Credentials = new(
        SecretKey: "secret",
        WebhookSecret: WebhookId,
        PublishableKey: "client-id",
        ApiBaseUrl: "https://api-m.sandbox.paypal.com"
    );

    // Sin espacios normalizados a propósito: la verificación debe reenviarlo byte a byte.
    private const string RefundEvent = """
        {"id":"WH-REFUND-1","event_version":"1.0","resource_type":"refund","event_type":"PAYMENT.CAPTURE.REFUNDED",
         "summary":"A $ 40.00 USD capture payment was refunded","resource":{"id":"REF-1","status":"COMPLETED",
         "amount":{"value":"40.00","currency_code":"USD"},
         "seller_payable_breakdown":{"total_refunded_amount":{"value":"60.00","currency_code":"USD"}},
         "links":[{"href":"https://api-m.paypal.com/v2/payments/refunds/REF-1","rel":"self","method":"GET"},
                  {"href":"https://api-m.paypal.com/v2/payments/captures/CAP-999","rel":"up","method":"GET"}]}}
        """;

    public static Dictionary<string, string> SignedHeaders() =>
        new()
        {
            // Mayúsculas mezcladas: HTTP no las distingue.
            ["paypal-auth-algo"] = "SHA256withRSA",
            ["PAYPAL-CERT-URL"] = "https://api.paypal.com/v1/notifications/certs/CERT-360caa42",
            ["PAYPAL-TRANSMISSION-ID"] = "69cd13f0-d67a-11e5-baa3-778b57f0f5d4",
            ["PAYPAL-TRANSMISSION-SIG"] = "lmI95Jx3Y9nhR5SJWlHVIWpg4AgFk7n9bCHSRxbrd8A9zrhdu2rMyFrmz+Zjh3s3",
            ["PAYPAL-TRANSMISSION-TIME"] = "2026-11-03T12:00:00Z",
        };

    [Fact]
    public async Task A_SUCCESS_verification_accepts_the_event_and_posts_back_the_raw_event_with_the_webhook_id()
    {
        var handler = new PayPalStub(VerificationResponse("SUCCESS"));
        var adapter = CreateAdapter(handler);

        var result = await adapter.VerifyWebhookSignatureAsync(
            RefundEvent,
            SignedHeaders(),
            Credentials,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("WH-REFUND-1", result.Value.ProviderEventId);
        Assert.Equal("PAYMENT.CAPTURE.REFUNDED", result.Value.EventType);
        Assert.Equal(SignedHeaders()["PAYPAL-TRANSMISSION-SIG"], result.Value.Signature);

        var verify = Assert.Single(handler.Requests, r => r.Path == "/v1/notifications/verify-webhook-signature");
        Assert.Equal("Bearer A21AA", verify.Authorization);
        // El evento va tal cual llegó (PayPal no acepta uno re-serializado).
        Assert.Contains(RefundEvent, verify.Body, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(verify.Body);
        var root = body.RootElement;
        Assert.Equal(WebhookId, root.GetProperty("webhook_id").GetString());
        Assert.Equal("SHA256withRSA", root.GetProperty("auth_algo").GetString());
        Assert.Equal(SignedHeaders()["PAYPAL-CERT-URL"], root.GetProperty("cert_url").GetString());
        Assert.Equal(SignedHeaders()["PAYPAL-TRANSMISSION-ID"], root.GetProperty("transmission_id").GetString());
        Assert.Equal(SignedHeaders()["PAYPAL-TRANSMISSION-SIG"], root.GetProperty("transmission_sig").GetString());
        Assert.Equal(SignedHeaders()["PAYPAL-TRANSMISSION-TIME"], root.GetProperty("transmission_time").GetString());
        Assert.Equal("WH-REFUND-1", root.GetProperty("webhook_event").GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_FAILURE_verification_rejects_the_event()
    {
        var adapter = CreateAdapter(new PayPalStub(VerificationResponse("FAILURE")));

        var result = await adapter.VerifyWebhookSignatureAsync(
            RefundEvent,
            SignedHeaders(),
            Credentials,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.WebhookSignature.Invalid", result.Error.Code);
    }

    [Fact]
    public async Task PayPal_answering_an_error_means_unverified_so_the_event_is_not_processed()
    {
        var adapter = CreateAdapter(new PayPalStub(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var result = await adapter.VerifyWebhookSignatureAsync(
            RefundEvent,
            SignedHeaders(),
            Credentials,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.WebhookSignature.Unavailable", result.Error.Code);
    }

    [Fact]
    public async Task PayPal_unreachable_means_unverified_so_the_event_is_not_processed()
    {
        var adapter = CreateAdapter(new PayPalStub(verifyResponse: null, throwOnVerify: true));

        var result = await adapter.VerifyWebhookSignatureAsync(
            RefundEvent,
            SignedHeaders(),
            Credentials,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.WebhookSignature.Unavailable", result.Error.Code);
    }

    [Fact]
    public async Task Missing_signature_headers_fail_without_calling_PayPal()
    {
        var handler = new PayPalStub(VerificationResponse("SUCCESS"));
        var headers = SignedHeaders();
        headers.Remove("PAYPAL-TRANSMISSION-SIG");

        var result = await CreateAdapter(handler)
            .VerifyWebhookSignatureAsync(RefundEvent, headers, Credentials, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.WebhookHeaders.Missing", result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Without_webhook_id_nothing_is_verified()
    {
        var handler = new PayPalStub(VerificationResponse("SUCCESS"));

        var result = await CreateAdapter(handler)
            .VerifyWebhookSignatureAsync(
                RefundEvent,
                SignedHeaders(),
                Credentials with
                {
                    WebhookSecret = null,
                },
                CancellationToken.None
            );

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.Webhook.WebhookIdMissing", result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_refund_maps_to_Refunded_on_the_capture_with_the_total_refunded_so_far()
    {
        var result = await Parse(RefundEvent, "PAYMENT.CAPTURE.REFUNDED");

        Assert.True(result.IsSuccess);
        Assert.Equal("CAP-999", result.Value.ProviderChargeReference);
        Assert.Equal(PaymentStatus.Refunded, result.Value.Status);
        // Acumulado de la captura (no solo este reembolso), igual que charge.amount_refunded de Stripe.
        Assert.Equal(6000, result.Value.RefundedAmountCents);
    }

    [Fact]
    public async Task A_refund_takes_the_capture_from_related_ids_when_present()
    {
        const string payload = """
            {"id":"WH-REFUND-2","resource_type":"refund","event_type":"PAYMENT.CAPTURE.REFUNDED",
             "resource":{"id":"REF-2","amount":{"value":"15.50","currency_code":"USD"},
                         "supplementary_data":{"related_ids":{"capture_id":"CAP-777"}}}}
            """;

        var result = await Parse(payload, "PAYMENT.CAPTURE.REFUNDED");

        Assert.True(result.IsSuccess);
        Assert.Equal("CAP-777", result.Value.ProviderChargeReference);
        Assert.Equal(1550, result.Value.RefundedAmountCents);
    }

    [Fact]
    public async Task A_reversal_maps_to_ChargedBack_on_the_capture()
    {
        const string payload = """
            {"id":"WH-REV-1","resource_type":"refund","event_type":"PAYMENT.CAPTURE.REVERSED",
             "resource":{"id":"REF-3","amount":{"value":"150.00","currency_code":"USD"},
                         "links":[{"href":"https://api-m.paypal.com/v2/payments/captures/CAP-999","rel":"up"}]}}
            """;

        var result = await Parse(payload, "PAYMENT.CAPTURE.REVERSED");

        Assert.True(result.IsSuccess);
        Assert.Equal("CAP-999", result.Value.ProviderChargeReference);
        Assert.Equal(PaymentStatus.ChargedBack, result.Value.Status);
        Assert.Equal("PAYMENT.CAPTURE.REVERSED", result.Value.FailureCode);
    }

    [Fact]
    public async Task A_new_dispute_maps_to_ChargedBack_on_the_disputed_capture()
    {
        const string payload = """
            {"id":"WH-DSP-1","resource_type":"dispute","event_type":"CUSTOMER.DISPUTE.CREATED",
             "resource":{"dispute_id":"PP-D-1","reason":"MERCHANDISE_OR_SERVICE_NOT_RECEIVED","status":"OPEN",
                         "disputed_transactions":[{"buyer_transaction_id":"BUY-1","seller_transaction_id":"CAP-999"}]}}
            """;

        var result = await Parse(payload, "CUSTOMER.DISPUTE.CREATED");

        Assert.True(result.IsSuccess);
        Assert.Equal("CAP-999", result.Value.ProviderChargeReference);
        Assert.Equal(PaymentStatus.ChargedBack, result.Value.Status);
        Assert.Equal("MERCHANDISE_OR_SERVICE_NOT_RECEIVED", result.Value.FailureCode);
    }

    [Fact]
    public async Task An_event_the_adapter_does_not_process_is_reported_as_unsupported()
    {
        const string payload = """{"id":"WH-X","event_type":"PAYMENT.CAPTURE.COMPLETED","resource":{"id":"CAP-1"}}""";

        var result = await Parse(payload, "PAYMENT.CAPTURE.COMPLETED");

        Assert.True(result.IsFailure);
        Assert.Equal(WebhookEventPayload.UnsupportedEventTypeCode, result.Error.Code);
    }

    [Fact]
    public async Task A_refund_without_a_capture_reference_is_an_unexpected_payload()
    {
        const string payload = """
            {"id":"WH-R","resource_type":"refund","event_type":"PAYMENT.CAPTURE.REFUNDED",
             "resource":{"id":"REF-9","amount":{"value":"1.00","currency_code":"USD"}}}
            """;

        var result = await Parse(payload, "PAYMENT.CAPTURE.REFUNDED");

        Assert.True(result.IsFailure);
        Assert.Equal("PayPal.Webhook.UnexpectedPayload", result.Error.Code);
    }

    // ---- helpers ----

    private static Task<BuildingBlocks.Results.Result<WebhookEventPayload>> Parse(string payload, string eventType) =>
        CreateAdapter(new PayPalStub(null)).ParseWebhookEventAsync(payload, eventType, CancellationToken.None);

    public static HttpResponseMessage VerificationResponse(string status) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"verification_status":"{{status}}"}""",
                Encoding.UTF8,
                "application/json"
            ),
        };

    private static PayPalPaymentAdapter CreateAdapter(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), NullLogger<PayPalPaymentAdapter>.Instance);
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>PayPal falso: token OAuth fijo y respuesta configurable para la verificación; anota cada llamada.</summary>
internal sealed class PayPalStub(HttpResponseMessage? verifyResponse, bool throwOnVerify = false) : HttpMessageHandler
{
    public sealed record Call(string Path, string? Authorization, string Body);

    public List<Call> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        Requests.Add(new Call(path, request.Headers.Authorization?.ToString(), body));

        if (path.EndsWith("/v1/oauth2/token", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "access_token": "A21AA", "token_type": "Bearer", "expires_in": 32400 }""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };

        if (throwOnVerify)
            throw new HttpRequestException("PayPal is down.");

        return verifyResponse ?? throw new InvalidOperationException($"Unexpected PayPal call to {path}.");
    }
}
