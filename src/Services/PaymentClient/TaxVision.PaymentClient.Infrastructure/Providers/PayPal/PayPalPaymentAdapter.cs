using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Providers.PayPal;

/// <summary>
/// Adapter PayPal (Orders v2). El flujo de PayPal NO es "token de tarjeta → confirmar en server" como
/// Stripe: el frontend usa los PayPal Buttons (crear + aprobar la orden) y nos manda el <c>orderId</c>
/// YA APROBADO como <see cref="ChargeAuthorizationRequest.PaymentMethod"/>; este adapter hace el
/// <b>capture</b> de esa orden. Así encaja en el mismo contrato <see cref="IPaymentProvider"/> sin
/// endpoints nuevos.
///
/// <para>Multi-tenant: cada tenant trae su propio <c>client-id</c> (PublishableKey) + <c>secret</c> y,
/// opcionalmente, su <c>ApiBaseUrl</c> (sandbox <c>https://api-m.sandbox.paypal.com</c> vs live
/// <c>https://api-m.paypal.com</c>). Se hace OAuth client-credentials por llamada; nada se cachea en
/// estado de instancia (el adapter es Singleton).</para>
/// </summary>
[PaymentProvider(PaymentProviderCode.PayPal)]
public sealed class PayPalPaymentAdapter(IHttpClientFactory httpClientFactory, ILogger<PayPalPaymentAdapter> logger)
    : IPaymentProvider
{
    private const string DefaultBaseUrl = "https://api-m.paypal.com";

    public PaymentProviderCode Code => PaymentProviderCode.PayPal;

    public async Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        TenantProviderCredentials credentials,
        ChargeAuthorizationRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(credentials.PublishableKey))
            return Result.Failure<ChargeAuthorizationResult>(
                new Error("PayPal.MissingClientId", "PayPal requires a client-id (publishable key).")
            );

        var baseUrl = ResolveBaseUrl(credentials.ApiBaseUrl);
        var orderId = request.PaymentMethod.Token;

        var tokenResult = await GetAccessTokenAsync(baseUrl, credentials, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<ChargeAuthorizationResult>(tokenResult.Error);

        try
        {
            using var http = httpClientFactory.CreateClient();
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture"
            );
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);
            // Idempotencia PayPal: reintentar el mismo capture no cobra dos veces.
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", request.IdempotencyKey.Value);
            message.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var (code, detail) = ReadPayPalError(body);
                logger.LogWarning(
                    "PayPal capture failed ({Status}) for order {OrderId}: {Code} {Detail}",
                    (int)response.StatusCode,
                    orderId,
                    code,
                    detail
                );
                return Result.Success(
                    new ChargeAuthorizationResult(
                        ProviderChargeReference: orderId,
                        Status: PaymentStatus.Failed,
                        FailureCode: code ?? $"paypal_http_{(int)response.StatusCode}",
                        FailureMessage: detail ?? "PayPal declined the capture."
                    )
                );
            }

            return MapCapture(body, orderId, request.Amount);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "PayPal capture threw for order {OrderId}.", orderId);
            return Result.Failure<ChargeAuthorizationResult>(new Error("PayPal.Capture.Failed", ex.Message));
        }
    }

    private static Result<ChargeAuthorizationResult> MapCapture(string body, string orderId, Money expected)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;

        // La captura vive en purchase_units[0].payments.captures[0].
        JsonElement capture = default;
        var hasCapture = false;
        if (
            root.TryGetProperty("purchase_units", out var units)
            && units.ValueKind == JsonValueKind.Array
            && units.GetArrayLength() > 0
            && units[0].TryGetProperty("payments", out var payments)
            && payments.TryGetProperty("captures", out var captures)
            && captures.ValueKind == JsonValueKind.Array
            && captures.GetArrayLength() > 0
        )
        {
            capture = captures[0];
            hasCapture = true;
        }

        var captureId = hasCapture && capture.TryGetProperty("id", out var cid) ? cid.GetString() : null;
        var captureStatus = hasCapture && capture.TryGetProperty("status", out var cst) ? cst.GetString() : status;
        var reference = string.IsNullOrEmpty(captureId) ? orderId : captureId!;

        // Defensa: el monto capturado debe coincidir con lo que esperábamos cobrar.
        if (hasCapture && capture.TryGetProperty("amount", out var amount))
        {
            var value = amount.TryGetProperty("value", out var v) ? v.GetString() : null;
            var currency = amount.TryGetProperty("currency_code", out var cc) ? cc.GetString() : null;
            var expectedValue = (expected.AmountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            if (
                !string.Equals(value, expectedValue, StringComparison.Ordinal)
                || !string.Equals(currency, expected.Currency, StringComparison.OrdinalIgnoreCase)
            )
                return Result.Success(
                    new ChargeAuthorizationResult(
                        reference,
                        PaymentStatus.Failed,
                        FailureCode: "paypal_amount_mismatch",
                        FailureMessage: $"Captured {value} {currency} but expected {expectedValue} {expected.Currency}."
                    )
                );
        }

        return captureStatus switch
        {
            "COMPLETED" => Result.Success(new ChargeAuthorizationResult(reference, PaymentStatus.Succeeded)),
            "PENDING" => Result.Success(new ChargeAuthorizationResult(reference, PaymentStatus.Processing)),
            _ => Result.Success(
                new ChargeAuthorizationResult(
                    reference,
                    PaymentStatus.Failed,
                    FailureCode: captureStatus ?? "paypal_not_completed",
                    FailureMessage: "PayPal order was not captured."
                )
            ),
        };
    }

    public async Task<Result<RefundResult>> RefundAsync(
        TenantProviderCredentials credentials,
        string providerChargeReference,
        Money amount,
        string reason,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(credentials.PublishableKey))
            return Result.Failure<RefundResult>(
                new Error("PayPal.MissingClientId", "PayPal requires a client-id (publishable key).")
            );

        var baseUrl = ResolveBaseUrl(credentials.ApiBaseUrl);
        var tokenResult = await GetAccessTokenAsync(baseUrl, credentials, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<RefundResult>(tokenResult.Error);

        try
        {
            using var http = httpClientFactory.CreateClient();
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/v2/payments/captures/{Uri.EscapeDataString(providerChargeReference)}/refund"
            );
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", Guid.NewGuid().ToString("N"));
            var value = (amount.AmountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            var payload = $"{{\"amount\":{{\"value\":\"{value}\",\"currency_code\":\"{amount.Currency}\"}}}}";
            message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var (code, detail) = ReadPayPalError(body);
                logger.LogWarning("PayPal refund failed for capture {Reference}: {Code} {Detail}", providerChargeReference, code, detail);
                return Result.Failure<RefundResult>(new Error("PayPal.Refund.Failed", detail ?? "PayPal refund failed."));
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var refundId = root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
            var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
            var mapped = status == "COMPLETED" ? PaymentStatus.Refunded : PaymentStatus.Processing;
            return Result.Success(new RefundResult(refundId, mapped, amount));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "PayPal refund threw for capture {Reference}.", providerChargeReference);
            return Result.Failure<RefundResult>(new Error("PayPal.Refund.Failed", ex.Message));
        }
    }

    /// <summary>
    /// PayPal firma sus webhooks con un esquema de múltiples headers (transmission-id/-time/-sig,
    /// cert-url, auth-algo) + verificación server-side contra su API — no encaja en el contrato
    /// de "un solo header de firma" de <see cref="IPaymentProvider"/>. El cobro de PayPal se confirma
    /// SÍNCRONO en <see cref="AuthorizeChargeAsync"/> (capture), así que el flujo principal no depende
    /// de webhooks; los eventos async (refund/dispute vía webhook) quedan para una fase posterior.
    /// </summary>
    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload,
        string signatureHeader,
        string webhookSecret,
        CancellationToken ct
    ) =>
        Task.FromResult(
            Result.Failure<WebhookVerificationResult>(
                new Error("PayPal.Webhook.NotSupported", "PayPal webhooks are not processed yet; capture is synchronous.")
            )
        );

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    ) =>
        Task.FromResult(
            Result.Failure<WebhookEventPayload>(
                new Error("PayPal.Webhook.NotSupported", "PayPal webhooks are not processed yet.")
            )
        );

    private async Task<Result<string>> GetAccessTokenAsync(
        string baseUrl,
        TenantProviderCredentials credentials,
        CancellationToken ct
    )
    {
        try
        {
            using var http = httpClientFactory.CreateClient();
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/oauth2/token");
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{credentials.PublishableKey}:{credentials.SecretKey}")
            );
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            message.Content = new FormUrlEncodedContent(
                new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") }
            );

            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("PayPal OAuth failed ({Status}).", (int)response.StatusCode);
                return Result.Failure<string>(
                    new Error("PayPal.Auth.Failed", "Could not authenticate with PayPal (check client-id/secret and URL).")
                );
            }

            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            return string.IsNullOrEmpty(token)
                ? Result.Failure<string>(new Error("PayPal.Auth.Failed", "PayPal did not return an access token."))
                : Result.Success(token!);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "PayPal OAuth threw.");
            return Result.Failure<string>(new Error("PayPal.Auth.Failed", ex.Message));
        }
    }

    private static string ResolveBaseUrl(string? apiBaseUrl) =>
        string.IsNullOrWhiteSpace(apiBaseUrl) ? DefaultBaseUrl : apiBaseUrl.Trim().TrimEnd('/');

    private static (string? Code, string? Detail) ReadPayPalError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? detail = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            if (
                root.TryGetProperty("details", out var details)
                && details.ValueKind == JsonValueKind.Array
                && details.GetArrayLength() > 0
                && details[0].TryGetProperty("description", out var d)
            )
                detail = d.GetString() ?? detail;
            return (code, detail);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
