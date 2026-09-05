using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Caching;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

public sealed class PayPalGateway(
    HttpClient http,
    IOptions<PayPalOptions> options,
    ICacheService cache,
    TimeProvider timeProvider,
    ILogger<PayPalGateway> logger
)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private PayPalOptions Options => options.Value;

    public async Task<Result<PayPalCreateOrderResult>> CreateOrderAsync(
        PayPalCreateOrderRequest request,
        CancellationToken ct
    )
    {
        var tokenResult = await GetAccessTokenAsync(ct);
        if (tokenResult.IsFailure)
            return Result.Failure<PayPalCreateOrderResult>(tokenResult.Error);

        using var message = new HttpRequestMessage(HttpMethod.Post, "v2/checkout/orders");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value.AccessToken);
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", request.IdempotencyKey);
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        message.Content = JsonContent.Create(BuildCreateOrderPayload(request), options: JsonOptions);

        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "PayPal create order returned HTTP {StatusCode}. Body={Body}",
                (int)response.StatusCode,
                TrimForLog(errorBody)
            );
            return Result.Failure<PayPalCreateOrderResult>(
                new Error("PayPal.CheckoutSession.Failed", $"PayPal returned HTTP {(int)response.StatusCode}.")
            );
        }

        var order = await response.Content.ReadFromJsonAsync<PayPalOrderResponse>(JsonOptions, ct);
        if (order is null || string.IsNullOrWhiteSpace(order.Id))
            return Result.Failure<PayPalCreateOrderResult>(
                new Error("PayPal.CheckoutSession.EmptyResponse", "PayPal returned an empty order response.")
            );

        var approvalUrl = FindApprovalUrl(order);
        if (string.IsNullOrWhiteSpace(approvalUrl))
            return Result.Failure<PayPalCreateOrderResult>(
                new Error("PayPal.CheckoutSession.MissingApprovalLink", "PayPal did not return a payer approval link.")
            );

        return Result.Success(new PayPalCreateOrderResult(order.Id, approvalUrl, order.Status ?? "CREATED"));
    }

    public async Task<Result<PayPalVerifyWebhookSignatureResult>> VerifyWebhookSignatureAsync(
        PayPalVerifyWebhookSignatureRequest request,
        CancellationToken ct
    )
    {
        JsonElement webhookEvent;
        try
        {
            using var document = JsonDocument.Parse(request.RawPayload);
            webhookEvent = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "PayPal webhook payload could not be parsed before signature verification.");
            return Result.Failure<PayPalVerifyWebhookSignatureResult>(
                new Error("PayPal.Webhook.ParseFailed", "PayPal webhook payload is not valid JSON.")
            );
        }

        var tokenResult = await GetAccessTokenAsync(ct);
        if (tokenResult.IsFailure)
            return Result.Failure<PayPalVerifyWebhookSignatureResult>(tokenResult.Error);

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/notifications/verify-webhook-signature");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value.AccessToken);
        message.Content = JsonContent.Create(
            new
            {
                auth_algo = request.AuthAlgo,
                cert_url = request.CertUrl,
                transmission_id = request.TransmissionId,
                transmission_sig = request.TransmissionSignature,
                transmission_time = request.TransmissionTime,
                webhook_id = request.WebhookId,
                webhook_event = webhookEvent,
            },
            options: JsonOptions
        );

        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("PayPal webhook verification returned HTTP {StatusCode}", (int)response.StatusCode);
            return Result.Failure<PayPalVerifyWebhookSignatureResult>(
                new Error(
                    "PayPal.WebhookSignature.VerificationFailed",
                    $"PayPal webhook verification returned HTTP {(int)response.StatusCode}."
                )
            );
        }

        var verification = await response.Content.ReadFromJsonAsync<PayPalWebhookSignatureVerificationResponse>(
            JsonOptions,
            ct
        );
        if (verification is null || string.IsNullOrWhiteSpace(verification.VerificationStatus))
            return Result.Failure<PayPalVerifyWebhookSignatureResult>(
                new Error(
                    "PayPal.WebhookSignature.EmptyResponse",
                    "PayPal returned an empty webhook verification response."
                )
            );

        return Result.Success(
            new PayPalVerifyWebhookSignatureResult(
                string.Equals(verification.VerificationStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase)
            )
        );
    }

    public async Task<Result<PayPalOrderStatusResult>> GetOrderAsync(string orderId, CancellationToken ct)
    {
        var tokenResult = await GetAccessTokenAsync(ct);
        if (tokenResult.IsFailure)
            return Result.Failure<PayPalOrderStatusResult>(tokenResult.Error);

        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"v2/checkout/orders/{Uri.EscapeDataString(orderId)}"
        );
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value.AccessToken);

        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("PayPal get order returned HTTP {StatusCode}", (int)response.StatusCode);
            return Result.Failure<PayPalOrderStatusResult>(
                new Error("PayPal.ChargeStatus.Failed", $"PayPal returned HTTP {(int)response.StatusCode}.")
            );
        }

        return await ReadOrderStatusAsync(response, "PayPal.ChargeStatus.EmptyResponse", ct);
    }

    public async Task<Result<PayPalOrderStatusResult>> CaptureOrderAsync(
        string orderId,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        var tokenResult = await GetAccessTokenAsync(ct);
        if (tokenResult.IsFailure)
            return Result.Failure<PayPalOrderStatusResult>(tokenResult.Error);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture"
        );
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value.AccessToken);
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        message.Content = JsonContent.Create(new { }, options: JsonOptions);

        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("PayPal capture order returned HTTP {StatusCode}", (int)response.StatusCode);
            return Result.Failure<PayPalOrderStatusResult>(
                new Error("PayPal.Capture.Failed", $"PayPal returned HTTP {(int)response.StatusCode}.")
            );
        }

        return await ReadOrderStatusAsync(response, "PayPal.Capture.EmptyResponse", ct);
    }

    public async Task<Result<PayPalRefundCaptureResult>> RefundCaptureAsync(
        PayPalRefundCaptureRequest request,
        CancellationToken ct
    )
    {
        var tokenResult = await GetAccessTokenAsync(ct);
        if (tokenResult.IsFailure)
            return Result.Failure<PayPalRefundCaptureResult>(tokenResult.Error);

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"v2/payments/captures/{Uri.EscapeDataString(request.CaptureId)}/refund"
        );
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value.AccessToken);
        message.Headers.TryAddWithoutValidation("PayPal-Request-Id", request.IdempotencyKey);
        message.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        message.Content = JsonContent.Create(BuildRefundCapturePayload(request), options: JsonOptions);

        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "PayPal refund capture returned HTTP {StatusCode}. Body={Body}",
                (int)response.StatusCode,
                TrimForLog(errorBody)
            );
            return Result.Failure<PayPalRefundCaptureResult>(
                new Error("PayPal.Refund.Failed", $"PayPal returned HTTP {(int)response.StatusCode}.")
            );
        }

        var refund = await response.Content.ReadFromJsonAsync<PayPalRefundResponse>(JsonOptions, ct);
        if (refund is null || string.IsNullOrWhiteSpace(refund.Id) || string.IsNullOrWhiteSpace(refund.Status))
            return Result.Failure<PayPalRefundCaptureResult>(
                new Error("PayPal.Refund.EmptyResponse", "PayPal returned an empty refund response.")
            );

        return Result.Success(new PayPalRefundCaptureResult(refund.Id, refund.Status));
    }

    private async Task<Result<PayPalAccessToken>> GetAccessTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Options.ClientId) || string.IsNullOrWhiteSpace(Options.ClientSecret))
            return Result.Failure<PayPalAccessToken>(
                new Error("PayPal.ConfigurationMissing", "PayPal client credentials are not configured.")
            );

        var cacheKey = BuildAccessTokenCacheKey();
        var cached = await cache.GetAsync<PayPalCachedAccessToken>(cacheKey, ct);
        if (cached is not null && cached.ExpiresAtUtc > timeProvider.GetUtcNow().AddMinutes(1))
            return Result.Success(new PayPalAccessToken(cached.AccessToken, "Bearer", 0));

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
        var rawCredentials = $"{Options.ClientId}:{Options.ClientSecret}";
        var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCredentials));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedCredentials);
        message.Content = new FormUrlEncodedContent(
            new Dictionary<string, string> { ["grant_type"] = "client_credentials" }
        );

        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("PayPal OAuth returned HTTP {StatusCode}", (int)response.StatusCode);
            return Result.Failure<PayPalAccessToken>(
                new Error("PayPal.AuthenticationFailed", "PayPal access token request failed.")
            );
        }

        var token = await response.Content.ReadFromJsonAsync<PayPalAccessToken>(JsonOptions, ct);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return Result.Failure<PayPalAccessToken>(
                new Error("PayPal.AuthenticationEmptyResponse", "PayPal returned an empty access token response.")
            );

        var cacheTtl = ResolveAccessTokenTtl(token.ExpiresIn);
        await cache.SetAsync(
            cacheKey,
            new PayPalCachedAccessToken(token.AccessToken, timeProvider.GetUtcNow().Add(cacheTtl)),
            cacheTtl,
            ct
        );

        return Result.Success(token);
    }

    private TimeSpan ResolveAccessTokenTtl(int expiresInSeconds)
    {
        if (expiresInSeconds <= 0)
            return Options.AccessTokenCacheTtl;

        if (expiresInSeconds <= 120)
            return TimeSpan.FromSeconds(Math.Max(1, expiresInSeconds / 2));

        var providerTtl = TimeSpan.FromSeconds(expiresInSeconds - 60);
        return providerTtl < Options.AccessTokenCacheTtl ? providerTtl : Options.AccessTokenCacheTtl;
    }

    private string BuildAccessTokenCacheKey()
    {
        var source = $"{Options.BaseUrl.Trim().ToLowerInvariant()}|{Options.ClientId.Trim()}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return $"paypal:oauth:access-token:{hash}";
    }

    private static object BuildCreateOrderPayload(PayPalCreateOrderRequest request)
    {
        var customId = request.Metadata.TryGetValue("onboardingId", out var onboardingId)
            ? onboardingId
            : request.IdempotencyKey;

        return new
        {
            intent = "CAPTURE",
            payment_source = new
            {
                paypal = new
                {
                    experience_context = new
                    {
                        payment_method_preference = "IMMEDIATE_PAYMENT_REQUIRED",
                        landing_page = "LOGIN",
                        shipping_preference = "NO_SHIPPING",
                        user_action = "PAY_NOW",
                        return_url = request.ReturnUrl,
                        cancel_url = request.CancelUrl,
                    },
                },
            },
            purchase_units = new[]
            {
                new
                {
                    reference_id = customId,
                    custom_id = customId,
                    description = request.Description,
                    amount = new
                    {
                        currency_code = request.CurrencyCode.ToUpperInvariant(),
                        value = request.AmountValue,
                    },
                },
            },
        };
    }

    private static object BuildRefundCapturePayload(PayPalRefundCaptureRequest request) =>
        new { amount = new { currency_code = request.CurrencyCode.ToUpperInvariant(), value = request.AmountValue } };

    private static string? FindApprovalUrl(PayPalOrderResponse order)
    {
        var links = order.Links ?? [];
        return links.FirstOrDefault(IsPayerActionLink)?.Href ?? links.FirstOrDefault(IsApproveLink)?.Href;
    }

    private static bool IsPayerActionLink(PayPalLink link) =>
        string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)
        && IsGetLink(link)
        && !string.IsNullOrWhiteSpace(link.Href);

    private static bool IsApproveLink(PayPalLink link) =>
        string.Equals(link.Rel, "approve", StringComparison.OrdinalIgnoreCase)
        && IsGetLink(link)
        && !string.IsNullOrWhiteSpace(link.Href);

    private static bool IsGetLink(PayPalLink link) =>
        string.IsNullOrWhiteSpace(link.Method) || string.Equals(link.Method, "GET", StringComparison.OrdinalIgnoreCase);

    public static string FormatAmount(long amountCents) =>
        (amountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static async Task<Result<PayPalOrderStatusResult>> ReadOrderStatusAsync(
        HttpResponseMessage response,
        string emptyResponseErrorCode,
        CancellationToken ct
    )
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        var orderId = TryGetString(root, "id");
        var orderStatus = TryGetString(root, "status");
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(orderStatus))
            return Result.Failure<PayPalOrderStatusResult>(
                new Error(emptyResponseErrorCode, "PayPal returned an invalid order response.")
            );

        var capture = FindFirstCapture(root);
        var captureId = capture.HasValue ? TryGetString(capture.Value, "id") : null;
        var captureStatus = capture.HasValue ? TryGetString(capture.Value, "status") : null;
        var failureReason = capture.HasValue
            ? TryGetString(capture.Value, "status_details", "reason")
            : TryGetString(root, "status_details", "reason");

        return Result.Success(
            new PayPalOrderStatusResult(orderId, orderStatus, captureId, captureStatus, failureReason)
        );
    }

    private static JsonElement? FindFirstCapture(JsonElement root)
    {
        if (
            !root.TryGetProperty("purchase_units", out var purchaseUnits)
            || purchaseUnits.ValueKind != JsonValueKind.Array
        )
            return null;

        foreach (var purchaseUnit in purchaseUnits.EnumerateArray())
        {
            if (
                !purchaseUnit.TryGetProperty("payments", out var payments)
                || !payments.TryGetProperty("captures", out var captures)
                || captures.ValueKind != JsonValueKind.Array
            )
                continue;

            foreach (var capture in captures.EnumerateArray())
                return capture.Clone();
        }

        return null;
    }

    private static string? TryGetString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string TrimForLog(string value) => value.Length <= 500 ? value : value[..500];
}
