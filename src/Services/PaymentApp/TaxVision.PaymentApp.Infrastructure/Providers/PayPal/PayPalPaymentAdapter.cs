using System.Globalization;
using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

[PaymentProvider(PaymentProviderCode.PayPal)]
public sealed class PayPalPaymentAdapter(PayPalGateway gateway, ILogger<PayPalPaymentAdapter> logger) : IPaymentProvider
{
    public PaymentProviderCode Code => PaymentProviderCode.PayPal;
    public ProviderCapabilities Capabilities => PayPalCapabilities.Instance;

    public Task<Result<ProviderCustomerToken>> GetOrCreateCustomerAsync(
        Guid tenantId,
        string email,
        string? name,
        CancellationToken ct
    ) =>
        Task.FromResult(
            Result.Failure<ProviderCustomerToken>(
                new Error(
                    "PayPal.Customer.NotSupported",
                    "PayPal checkout for onboarding does not require platform-side customer creation."
                )
            )
        );

    public Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        ChargeAuthorizationRequest request,
        CancellationToken ct
    ) =>
        Task.FromResult(
            Result.Failure<ChargeAuthorizationResult>(
                new Error(
                    "PayPal.Authorize.NotImplemented",
                    "PayPal direct charge authorization is not wired yet; use hosted checkout for onboarding."
                )
            )
        );

    public async Task<Result<CaptureResult>> CaptureAsync(
        string providerChargeReference,
        Money amount,
        CancellationToken ct
    )
    {
        var captured = await gateway.CaptureOrderAsync(
            providerChargeReference,
            BuildCaptureIdempotencyKey(providerChargeReference),
            ct
        );
        if (captured.IsFailure)
            return Result.Failure<CaptureResult>(captured.Error);

        var status = MapOrderStatus(captured.Value, providerChargeReference);
        return Result.Success(new CaptureResult(status.ProviderChargeReference, status.Status, amount));
    }

    public async Task<Result<RefundResult>> RefundAsync(
        string providerChargeReference,
        Money amount,
        string reason,
        CancellationToken ct
    )
    {
        var refund = await gateway.RefundCaptureAsync(
            new PayPalRefundCaptureRequest(
                providerChargeReference,
                PayPalGateway.FormatAmount(amount.AmountCents),
                amount.Currency,
                BuildRefundIdempotencyKey(providerChargeReference, amount)
            ),
            ct
        );
        if (refund.IsFailure)
            return Result.Failure<RefundResult>(refund.Error);

        return Result.Success(new RefundResult(refund.Value.RefundId, MapRefundStatus(refund.Value.Status), amount));
    }

    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        ProviderWebhookVerificationRequest request,
        CancellationToken ct
    ) => VerifyWebhookSignatureCoreAsync(request, ct);

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    )
    {
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;

            return Task.FromResult(
                eventType switch
                {
                    "CHECKOUT.ORDER.APPROVED" => Result.Success(
                        new WebhookEventPayload(
                            ProviderChargeReference: GetRequiredString(root, "resource", "id"),
                            Status: PaymentStatus.Processing,
                            FailureCode: null,
                            FailureMessage: null,
                            RefundedAmountCents: null
                        )
                    ),
                    "PAYMENT.CAPTURE.COMPLETED" => Result.Success(
                        new WebhookEventPayload(
                            ProviderChargeReference: GetRelatedOrderId(root)
                                ?? GetRequiredString(root, "resource", "id"),
                            Status: PaymentStatus.Succeeded,
                            FailureCode: null,
                            FailureMessage: null,
                            RefundedAmountCents: null,
                            ReconciledChargeReference: GetRequiredString(root, "resource", "id")
                        )
                    ),
                    "PAYMENT.CAPTURE.PENDING" => Result.Success(
                        new WebhookEventPayload(
                            ProviderChargeReference: GetRelatedOrderId(root)
                                ?? GetRequiredString(root, "resource", "id"),
                            Status: PaymentStatus.Processing,
                            FailureCode: null,
                            FailureMessage: null,
                            RefundedAmountCents: null
                        )
                    ),
                    "PAYMENT.CAPTURE.DENIED" or "PAYMENT.CAPTURE.DECLINED" => Result.Success(
                        new WebhookEventPayload(
                            ProviderChargeReference: GetRelatedOrderId(root)
                                ?? GetRequiredString(root, "resource", "id"),
                            Status: PaymentStatus.Failed,
                            FailureCode: eventType,
                            FailureMessage: GetStatusDetailsReason(root) ?? "PayPal declined the capture.",
                            RefundedAmountCents: null
                        )
                    ),
                    "PAYMENT.CAPTURE.REFUNDED" => Result.Success(
                        new WebhookEventPayload(
                            ProviderChargeReference: GetRelatedCaptureId(root)
                                ?? GetRequiredString(root, "resource", "id"),
                            Status: PaymentStatus.Refunded,
                            FailureCode: null,
                            FailureMessage: null,
                            RefundedAmountCents: GetAmountCents(root)
                        )
                    ),
                    "PAYMENT.CAPTURE.REVERSED" => Result.Success(
                        new WebhookEventPayload(
                            ProviderChargeReference: GetRelatedCaptureId(root)
                                ?? GetRequiredString(root, "resource", "id"),
                            Status: PaymentStatus.ChargedBack,
                            FailureCode: eventType,
                            FailureMessage: "PayPal reversed the capture.",
                            RefundedAmountCents: null
                        )
                    ),
                    _ => Result.Failure<WebhookEventPayload>(
                        new Error("PayPal.Webhook.UnsupportedEventType", $"Event type '{eventType}' is not handled.")
                    ),
                }
            );
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "PayPal webhook payload could not be parsed.");
            return Task.FromResult(
                Result.Failure<WebhookEventPayload>(new Error("PayPal.Webhook.ParseFailed", ex.Message))
            );
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "PayPal webhook payload is missing required data.");
            return Task.FromResult(
                Result.Failure<WebhookEventPayload>(new Error("PayPal.Webhook.UnexpectedPayload", ex.Message))
            );
        }
    }

    public async Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
        string providerChargeReference,
        CancellationToken ct
    )
    {
        var order = await gateway.GetOrderAsync(providerChargeReference, ct);
        if (order.IsFailure)
            return Result.Failure<ChargeAuthorizationResult>(order.Error);

        return Result.Success(MapOrderStatus(order.Value, providerChargeReference));
    }

    public async Task<Result<ChargeAuthorizationResult>> FinalizeHostedCheckoutAsync(
        string providerChargeReference,
        Money amount,
        CancellationToken ct
    )
    {
        var order = await gateway.GetOrderAsync(providerChargeReference, ct);
        if (order.IsFailure)
            return Result.Failure<ChargeAuthorizationResult>(order.Error);

        if (ShouldCaptureApprovedOrder(order.Value))
        {
            var capture = await gateway.CaptureOrderAsync(
                order.Value.OrderId,
                BuildCaptureIdempotencyKey(order.Value.OrderId),
                ct
            );
            if (capture.IsFailure)
                return Result.Failure<ChargeAuthorizationResult>(capture.Error);

            return Result.Success(MapOrderStatus(capture.Value, providerChargeReference));
        }

        return Result.Success(MapOrderStatus(order.Value, providerChargeReference));
    }

    public Task<Result<SetupIntentInfo>> CreateSetupIntentAsync(ProviderCustomerToken customer, CancellationToken ct) =>
        Task.FromResult(
            Result.Failure<SetupIntentInfo>(
                new Error("PayPal.SetupIntent.NotSupported", "PayPal does not use Stripe-style SetupIntents.")
            )
        );

    public Task<Result<SavedPaymentMethodInfo>> AttachPaymentMethodAsync(
        ProviderCustomerToken customer,
        string paymentMethodReference,
        CancellationToken ct
    ) =>
        Task.FromResult(
            Result.Failure<SavedPaymentMethodInfo>(
                new Error(
                    "PayPal.PaymentMethod.NotSupported",
                    "PayPal wallet payment methods are not attached through this interface."
                )
            )
        );

    public Task<Result> DetachPaymentMethodAsync(string paymentMethodReference, CancellationToken ct) =>
        Task.FromResult(
            Result.Failure(
                new Error(
                    "PayPal.PaymentMethod.NotSupported",
                    "PayPal wallet payment methods are not detached through this interface."
                )
            )
        );

    public async Task<Result<HostedCheckoutSessionResult>> CreateHostedCheckoutSessionAsync(
        HostedCheckoutSessionRequest request,
        CancellationToken ct
    )
    {
        if (request.Method != PaymentMethodKind.Wallet)
            return Result.Failure<HostedCheckoutSessionResult>(
                new Error(
                    "PayPal.CheckoutSession.MethodUnsupported",
                    "PayPal onboarding checkout supports only the Wallet payment method."
                )
            );

        var orderResult = await gateway.CreateOrderAsync(
            new PayPalCreateOrderRequest(
                AmountValue: PayPalGateway.FormatAmount(request.Amount.AmountCents),
                CurrencyCode: request.Amount.Currency,
                Description: request.Descriptor.Value,
                ReturnUrl: request.SuccessUrl,
                CancelUrl: request.CancelUrl,
                IdempotencyKey: request.IdempotencyKey.Value,
                Metadata: request.Metadata
            ),
            ct
        );

        if (orderResult.IsFailure)
        {
            logger.LogWarning(
                "PayPal hosted checkout creation failed. IdempotencyKey={IdempotencyKey} Error={ErrorCode}: {ErrorMessage}",
                request.IdempotencyKey.Value,
                orderResult.Error.Code,
                orderResult.Error.Message
            );
            return Result.Failure<HostedCheckoutSessionResult>(orderResult.Error);
        }

        return Result.Success(
            new HostedCheckoutSessionResult(
                ProviderSessionId: orderResult.Value.OrderId,
                ProviderPaymentIntentReference: orderResult.Value.OrderId,
                CheckoutUrl: orderResult.Value.ApprovalUrl
            )
        );
    }

    private async Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureCoreAsync(
        ProviderWebhookVerificationRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.WebhookId))
            return Result.Failure<WebhookVerificationResult>(
                new Error("PayPal.WebhookId.Missing", "PayPal webhook id is not configured.")
            );

        var requiredHeaders = ReadRequiredWebhookHeaders(request.Headers);
        if (requiredHeaders.IsFailure)
            return Result.Failure<WebhookVerificationResult>(requiredHeaders.Error);

        var headers = requiredHeaders.Value;
        var verification = await gateway.VerifyWebhookSignatureAsync(
            new PayPalVerifyWebhookSignatureRequest(
                AuthAlgo: headers.AuthAlgo,
                CertUrl: headers.CertUrl,
                TransmissionId: headers.TransmissionId,
                TransmissionSignature: headers.TransmissionSignature,
                TransmissionTime: headers.TransmissionTime,
                WebhookId: request.WebhookId,
                RawPayload: request.RawPayload
            ),
            ct
        );

        if (verification.IsFailure)
            return Result.Failure<WebhookVerificationResult>(verification.Error);

        if (!verification.Value.IsValid)
            return Result.Failure<WebhookVerificationResult>(
                new Error("PayPal.WebhookSignature.Invalid", "PayPal webhook signature verification failed.")
            );

        using var document = JsonDocument.Parse(request.RawPayload);
        var root = document.RootElement;
        var eventId = GetRequiredString(root, "id");
        var eventType = GetRequiredString(root, "event_type");

        return Result.Success(new WebhookVerificationResult(eventId, eventType, request.RawPayload));
    }

    private static Result<PayPalWebhookHeaders> ReadRequiredWebhookHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var authAlgo = GetHeader(headers, "PAYPAL-AUTH-ALGO");
        var certUrl = GetHeader(headers, "PAYPAL-CERT-URL");
        var transmissionId = GetHeader(headers, "PAYPAL-TRANSMISSION-ID");
        var transmissionSignature = GetHeader(headers, "PAYPAL-TRANSMISSION-SIG");
        var transmissionTime = GetHeader(headers, "PAYPAL-TRANSMISSION-TIME");

        if (
            string.IsNullOrWhiteSpace(authAlgo)
            || string.IsNullOrWhiteSpace(certUrl)
            || string.IsNullOrWhiteSpace(transmissionId)
            || string.IsNullOrWhiteSpace(transmissionSignature)
            || string.IsNullOrWhiteSpace(transmissionTime)
        )
            return Result.Failure<PayPalWebhookHeaders>(
                new Error("PayPal.WebhookHeaders.Missing", "One or more PayPal webhook signature headers are missing.")
            );

        return Result.Success(
            new PayPalWebhookHeaders(authAlgo, certUrl, transmissionId, transmissionSignature, transmissionTime)
        );
    }

    private static string? GetHeader(IReadOnlyDictionary<string, string> headers, string name)
    {
        if (headers.TryGetValue(name, out var value))
            return value;

        foreach (var (key, candidate) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    private static string GetRequiredString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
                throw new InvalidOperationException($"Missing JSON path '{string.Join(".", path)}'.");
        }

        return current.GetString()
            ?? throw new InvalidOperationException($"JSON path '{string.Join(".", path)}' is null.");
    }

    private static string? GetRelatedOrderId(JsonElement root) =>
        TryGetString(root, "resource", "supplementary_data", "related_ids", "order_id");

    private static string? GetRelatedCaptureId(JsonElement root) =>
        TryGetString(root, "resource", "supplementary_data", "related_ids", "capture_id");

    private static string? GetStatusDetailsReason(JsonElement root) =>
        TryGetString(root, "resource", "status_details", "reason");

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

    private static long? GetAmountCents(JsonElement root)
    {
        var value = TryGetString(root, "resource", "amount", "value");
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return null;

        return (long)Math.Round(parsed * 100m, MidpointRounding.AwayFromZero);
    }

    private static bool ShouldCaptureApprovedOrder(PayPalOrderStatusResult order) =>
        string.Equals(order.OrderStatus, "APPROVED", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrWhiteSpace(order.CaptureId);

    private static ChargeAuthorizationResult MapOrderStatus(PayPalOrderStatusResult order, string originalReference)
    {
        var status = string.IsNullOrWhiteSpace(order.CaptureStatus) ? order.OrderStatus : order.CaptureStatus;
        var normalized = status.ToUpperInvariant();
        var succeededReference = string.IsNullOrWhiteSpace(order.CaptureId) ? order.OrderId : order.CaptureId;
        var stableReference = string.IsNullOrWhiteSpace(originalReference) ? order.OrderId : originalReference;

        return normalized switch
        {
            "COMPLETED" => new ChargeAuthorizationResult(succeededReference, PaymentStatus.Succeeded),
            "PENDING" => new ChargeAuthorizationResult(stableReference, PaymentStatus.Processing),
            "DENIED" or "DECLINED" or "FAILED" => new ChargeAuthorizationResult(
                stableReference,
                PaymentStatus.Failed,
                FailureCode: $"PayPal.{normalized}",
                FailureMessage: order.FailureReason ?? "PayPal reported the capture as failed."
            ),
            "VOIDED" or "CANCELLED" => new ChargeAuthorizationResult(
                stableReference,
                PaymentStatus.Cancelled,
                FailureCode: $"PayPal.{normalized}",
                FailureMessage: order.FailureReason ?? "PayPal cancelled the order."
            ),
            "REFUNDED" => new ChargeAuthorizationResult(stableReference, PaymentStatus.Refunded),
            "REVERSED" => new ChargeAuthorizationResult(
                stableReference,
                PaymentStatus.ChargedBack,
                FailureCode: "PayPal.REVERSED",
                FailureMessage: order.FailureReason ?? "PayPal reversed the capture."
            ),
            _ => new ChargeAuthorizationResult(stableReference, PaymentStatus.Processing),
        };
    }

    private static string BuildCaptureIdempotencyKey(string orderId) => $"paypal-capture-{orderId}";

    private static string BuildRefundIdempotencyKey(string captureId, Money amount) =>
        $"paypal-refund-{captureId}-{amount.AmountCents}-{amount.Currency.ToUpperInvariant()}";

    private static PaymentStatus MapRefundStatus(string status) =>
        status.ToUpperInvariant() switch
        {
            "COMPLETED" => PaymentStatus.Refunded,
            "CANCELLED" or "DECLINED" or "DENIED" or "FAILED" => PaymentStatus.Failed,
            _ => PaymentStatus.Processing,
        };

    private sealed record PayPalWebhookHeaders(
        string AuthAlgo,
        string CertUrl,
        string TransmissionId,
        string TransmissionSignature,
        string TransmissionTime
    );
}
