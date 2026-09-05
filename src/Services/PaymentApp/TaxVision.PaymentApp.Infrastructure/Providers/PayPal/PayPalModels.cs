using System.Text.Json.Serialization;

namespace TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

public sealed record PayPalCreateOrderRequest(
    string AmountValue,
    string CurrencyCode,
    string Description,
    string ReturnUrl,
    string CancelUrl,
    string IdempotencyKey,
    IReadOnlyDictionary<string, string> Metadata
);

public sealed record PayPalCreateOrderResult(string OrderId, string ApprovalUrl, string Status);

public sealed record PayPalRefundCaptureRequest(
    string CaptureId,
    string AmountValue,
    string CurrencyCode,
    string IdempotencyKey
);

public sealed record PayPalRefundCaptureResult(string RefundId, string Status);

public sealed record PayPalVerifyWebhookSignatureRequest(
    string AuthAlgo,
    string CertUrl,
    string TransmissionId,
    string TransmissionSignature,
    string TransmissionTime,
    string WebhookId,
    string RawPayload
);

public sealed record PayPalVerifyWebhookSignatureResult(bool IsValid);

public sealed record PayPalOrderStatusResult(
    string OrderId,
    string OrderStatus,
    string? CaptureId,
    string? CaptureStatus,
    string? FailureReason
);

internal sealed record PayPalAccessToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn
);

internal sealed record PayPalOrderResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("links")] IReadOnlyList<PayPalLink>? Links
);

internal sealed record PayPalRefundResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("status")] string? Status
);

internal sealed record PayPalWebhookSignatureVerificationResponse(
    [property: JsonPropertyName("verification_status")] string? VerificationStatus
);

internal sealed record PayPalLink(
    [property: JsonPropertyName("href")] string? Href,
    [property: JsonPropertyName("rel")] string? Rel,
    [property: JsonPropertyName("method")] string? Method
);

internal sealed record PayPalCachedAccessToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
