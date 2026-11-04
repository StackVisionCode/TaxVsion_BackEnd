using System.Globalization;
using System.Net;
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
///
/// <para>Webhooks: reembolsos, reversos y disputas que la oficina ve en PayPal llegan por webhook y se
/// verifican con <c>POST /v1/notifications/verify-webhook-signature</c> (Webhook ID de la oficina).</para>
/// </summary>
[PaymentProvider(PaymentProviderCode.PayPal)]
public sealed class PayPalPaymentAdapter(IHttpClientFactory httpClientFactory, ILogger<PayPalPaymentAdapter> logger)
    : IPaymentProvider,
        IPaymentWebhookRegistration
{
    private const string WebhookUrlAlreadyExists = "WEBHOOK_URL_ALREADY_EXISTS";

    private const string DefaultBaseUrl = "https://api-m.paypal.com";
    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";

    private const string CaptureRefunded = "PAYMENT.CAPTURE.REFUNDED";
    private const string CaptureReversed = "PAYMENT.CAPTURE.REVERSED";
    private const string DisputeCreated = "CUSTOMER.DISPUTE.CREATED";

    private const string AuthAlgoHeader = "PAYPAL-AUTH-ALGO";
    private const string CertUrlHeader = "PAYPAL-CERT-URL";
    private const string TransmissionIdHeader = "PAYPAL-TRANSMISSION-ID";
    private const string TransmissionSigHeader = "PAYPAL-TRANSMISSION-SIG";
    private const string TransmissionTimeHeader = "PAYPAL-TRANSMISSION-TIME";

    public PaymentProviderCode Code => PaymentProviderCode.PayPal;

    // La captura es síncrona; reembolsos/reversos/disputas llegan por webhook y se verifican con el Webhook ID
    // (lo que guardamos como webhook secret). Opcional: sin él se cobra igual, pero esos webhooks se rechazan.
    public PaymentProviderDescriptor Descriptor { get; } =
        new(
            PaymentProviderCode.PayPal,
            DisplayName: "PayPal",
            PublicKeyLabel: "Client ID",
            SecretKeyLabel: "Client secret",
            UsesWebhookSecret: true,
            AcceptsApiBaseUrl: true,
            ApiBaseUrlHelp: $"Leave empty to use live ({DefaultBaseUrl}). Use {SandboxBaseUrl} with sandbox credentials.",
            ApiBaseUrlPlaceholder: SandboxBaseUrl,
            CredentialsHelpUrl: "https://developer.paypal.com/dashboard/applications",
            // Los mismos que entiende ParseWebhookEventAsync: la oficina los marca al crear el webhook.
            WebhookEvents: [CaptureRefunded, CaptureReversed, DisputeCreated],
            WebhookRouteName: PaymentWebhookRoutes.PayPal,
            WebhookSecretLabel: "Webhook ID"
        );

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
            // La respuesta por defecto es mínima (id, status, links): pedimos la orden completa para leer payer/payment_source.
            message.Headers.TryAddWithoutValidation("Prefer", "return=representation");
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
        var mapped = MapCaptureStatus(root, orderId, expected);
        return mapped.IsSuccess ? Result.Success(mapped.Value with { Payer = ReadPayer(root) }) : mapped;
    }

    /// <summary>
    /// Pagador según la orden capturada (Orders v2): <c>payer.email_address</c> y <c>payer.name</c>
    /// (<c>given_name</c> + <c>surname</c>), con respaldo en <c>payment_source.paypal</c>; si pagó con
    /// tarjeta, <c>payment_source.card</c> (<c>name</c>, <c>brand</c>, <c>last_digits</c>). Tolerante:
    /// lo que falte queda en null.
    /// </summary>
    private static ProviderPayerDetails? ReadPayer(JsonElement root)
    {
        var payer = ReadObject(root, "payer");
        var source = ReadObject(root, "payment_source");
        var wallet = source is { } s ? ReadObject(s, "paypal") : null;
        var card = source is { } c ? ReadObject(c, "card") : null;

        var details = new ProviderPayerDetails(
            Name: ReadFullName(payer) ?? ReadFullName(wallet) ?? ReadOptionalString(card, "name"),
            Email: ReadOptionalString(payer, "email_address") ?? ReadOptionalString(wallet, "email_address"),
            CardBrand: ReadOptionalString(card, "brand"),
            CardLast4: ReadOptionalString(card, "last_digits")
        );

        return details is { Name: null, Email: null, CardBrand: null, CardLast4: null } ? null : details;
    }

    private static string? ReadFullName(JsonElement? party)
    {
        if (party is not { } p || ReadObject(p, "name") is not { } name)
            return null;

        var full = string.Join(
            ' ',
            new[] { ReadString(name, "given_name"), ReadString(name, "surname") }.Where(part =>
                !string.IsNullOrWhiteSpace(part)
            )
        );
        return full.Length > 0 ? full : null;
    }

    private static JsonElement? ReadObject(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? ReadOptionalString(JsonElement? element, string property) =>
        element is { } e ? ReadString(e, property) : null;

    private static Result<ChargeAuthorizationResult> MapCaptureStatus(JsonElement root, string orderId, Money expected)
    {
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

    /// <summary>
    /// Estado actual del cobro: GET de la captura (Payments v2, <c>/v2/payments/captures/{id}</c>), que es lo
    /// que guardamos como referencia. Si PayPal no la conoce (404), la referencia es la orden (el capture no
    /// devolvió captura) y se lee la captura de <c>/v2/checkout/orders/{id}</c>.
    /// </summary>
    public async Task<Result<WebhookEventPayload>> GetChargeStatusAsync(
        TenantProviderCredentials credentials,
        ChargeStatusLookup lookup,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(credentials.PublishableKey))
            return Result.Failure<WebhookEventPayload>(
                new Error("PayPal.MissingClientId", "PayPal requires a client-id (publishable key).")
            );

        var baseUrl = ResolveBaseUrl(credentials.ApiBaseUrl);
        var tokenResult = await GetAccessTokenAsync(baseUrl, credentials, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<WebhookEventPayload>(tokenResult.Error);

        var reference = lookup.ProviderChargeReference;
        var escaped = Uri.EscapeDataString(reference);
        try
        {
            var (captureStatus, captureBody) = await GetAsync(
                $"{baseUrl}/v2/payments/captures/{escaped}",
                tokenResult.Value,
                ct
            );
            if (captureStatus == HttpStatusCode.OK)
            {
                using var captureDoc = JsonDocument.Parse(captureBody);
                return MapCaptureState(captureDoc.RootElement, reference);
            }

            if (captureStatus != HttpStatusCode.NotFound)
                return ChargeStatusFailed(captureStatus, captureBody);

            var (orderStatus, orderBody) = await GetAsync(
                $"{baseUrl}/v2/checkout/orders/{escaped}",
                tokenResult.Value,
                ct
            );
            if (orderStatus != HttpStatusCode.OK)
                return ChargeStatusFailed(orderStatus, orderBody);

            using var orderDoc = JsonDocument.Parse(orderBody);
            return MapOrderState(orderDoc.RootElement, reference);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "PayPal charge status lookup threw for reference {Reference}.", reference);
            return Result.Failure<WebhookEventPayload>(new Error("PayPal.ChargeStatus.Failed", ex.Message));
        }
    }

    private async Task<(HttpStatusCode Status, string Body)> GetAsync(
        string url,
        string accessToken,
        CancellationToken ct
    )
    {
        using var http = httpClientFactory.CreateClient();
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(message, ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    private Result<WebhookEventPayload> ChargeStatusFailed(HttpStatusCode status, string body)
    {
        var (code, detail) = ReadPayPalError(body);
        logger.LogWarning("PayPal charge status lookup failed ({Status}): {Code}", (int)status, code);
        return Result.Failure<WebhookEventPayload>(
            new Error("PayPal.ChargeStatus.Failed", detail ?? $"PayPal answered HTTP {(int)status}.")
        );
    }

    /// <summary>Orden sin captura: sigue pendiente salvo que PayPal la haya anulado (<c>VOIDED</c>).</summary>
    private static Result<WebhookEventPayload> MapOrderState(JsonElement order, string reference)
    {
        if (
            order.ValueKind == JsonValueKind.Object
            && order.TryGetProperty("purchase_units", out var units)
            && units.ValueKind == JsonValueKind.Array
            && units.GetArrayLength() > 0
            && ReadObject(units[0], "payments") is { } payments
            && payments.TryGetProperty("captures", out var captures)
            && captures.ValueKind == JsonValueKind.Array
            && captures.GetArrayLength() > 0
        )
            return MapCaptureState(captures[0], reference);

        var status = ReadString(order, "status");
        return Result.Success(
            new WebhookEventPayload(
                reference,
                status == "VOIDED" ? PaymentStatus.Cancelled : PaymentStatus.Processing,
                status,
                null,
                null
            )
        );
    }

    /// <summary>Captura (enum de Payments v2: COMPLETED, DECLINED, PARTIALLY_REFUNDED, PENDING, REFUNDED,
    /// FAILED). Un reembolso antes de confirmar no se aplica: no se puede dar el pago por cobrado.</summary>
    private static Result<WebhookEventPayload> MapCaptureState(JsonElement capture, string reference)
    {
        var status = ReadString(capture, "status");
        var reason = ReadObject(capture, "status_details") is { } details ? ReadString(details, "reason") : null;
        return status switch
        {
            "COMPLETED" => Result.Success(
                new WebhookEventPayload(
                    reference,
                    PaymentStatus.Succeeded,
                    null,
                    null,
                    null,
                    PaidAmountCents: ReadAmountCents(capture, "amount"),
                    PaidCurrency: ReadObject(capture, "amount") is { } amount
                        ? ReadString(amount, "currency_code")
                        : null
                )
            ),
            "DECLINED" or "FAILED" => Result.Success(
                new WebhookEventPayload(
                    reference,
                    PaymentStatus.Failed,
                    reason ?? status,
                    $"PayPal capture {status}.",
                    null
                )
            ),
            "PENDING" => Result.Success(
                new WebhookEventPayload(reference, PaymentStatus.Processing, reason, null, null)
            ),
            "REFUNDED" or "PARTIALLY_REFUNDED" => Result.Failure<WebhookEventPayload>(
                new Error(
                    "PayPal.ChargeStatus.RefundedBeforeConfirmation",
                    "The PayPal capture was refunded before the payment was confirmed; not applying."
                )
            ),
            _ => Result.Failure<WebhookEventPayload>(
                new Error("PayPal.ChargeStatus.UnexpectedPayload", "PayPal returned a capture without a known status.")
            ),
        };
    }

    /// <summary>
    /// POST /v1/notifications/webhooks con la URL y los eventos del descriptor. El <c>id</c> devuelto ES el
    /// Webhook ID que pide verify-webhook-signature, así que es lo que se guarda como secreto. Si PayPal
    /// responde <c>WEBHOOK_URL_ALREADY_EXISTS</c>, se lista y se reutiliza el de esa URL.
    /// </summary>
    public async Task<Result<RegisteredWebhook>> RegisterWebhookAsync(
        TenantProviderCredentials credentials,
        WebhookRegistrationRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(credentials.PublishableKey))
            return Result.Failure<RegisteredWebhook>(
                new Error("PayPal.MissingClientId", "PayPal requires a client-id (publishable key).")
            );

        var baseUrl = ResolveBaseUrl(credentials.ApiBaseUrl);
        var tokenResult = await GetAccessTokenAsync(baseUrl, credentials, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<RegisteredWebhook>(tokenResult.Error);

        var url = request.Url.AbsoluteUri;
        try
        {
            using var http = httpClientFactory.CreateClient();
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/notifications/webhooks");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);
            var payload = JsonSerializer.Serialize(
                new { url, event_types = request.Events.Select(name => new { name }).ToArray() }
            );
            message.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                return ReadString(doc.RootElement, "id") is { Length: > 0 } id
                    ? Result.Success(new RegisteredWebhook(id, id))
                    : Result.Failure<RegisteredWebhook>(
                        new Error("PayPal.Webhook.UnexpectedPayload", "PayPal did not return the webhook id.")
                    );
            }

            var (code, detail) = ReadPayPalError(body);
            if (code == WebhookUrlAlreadyExists)
                return await FindWebhookByUrlAsync(baseUrl, tokenResult.Value, url, ct);

            logger.LogWarning("PayPal webhook registration failed ({Status}): {Code}", (int)response.StatusCode, code);
            return Result.Failure<RegisteredWebhook>(RegistrationError(response.StatusCode, code, detail));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "PayPal webhook registration threw.");
            return Result.Failure<RegisteredWebhook>(new Error(WebhookRegistrationErrors.TransientCode, ex.Message));
        }
    }

    /// <summary>GET /v1/notifications/webhooks (los de la app) y el que coincide con la URL.</summary>
    private async Task<Result<RegisteredWebhook>> FindWebhookByUrlAsync(
        string baseUrl,
        string accessToken,
        string url,
        CancellationToken ct
    )
    {
        var (status, body) = await GetAsync($"{baseUrl}/v1/notifications/webhooks", accessToken, ct);
        if (status != HttpStatusCode.OK)
        {
            var (code, detail) = ReadPayPalError(body);
            return Result.Failure<RegisteredWebhook>(RegistrationError(status, code, detail));
        }

        using var doc = JsonDocument.Parse(body);
        if (
            doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("webhooks", out var webhooks)
            && webhooks.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var webhook in webhooks.EnumerateArray())
            {
                if (
                    string.Equals(ReadString(webhook, "url"), url, StringComparison.OrdinalIgnoreCase)
                    && ReadString(webhook, "id") is { Length: > 0 } id
                )
                    return Result.Success(new RegisteredWebhook(id, id));
            }
        }

        return Result.Failure<RegisteredWebhook>(
            new Error("PayPal.Webhook.NotFound", "PayPal reported the URL as registered but did not list it.")
        );
    }

    /// <summary>DELETE /v1/notifications/webhooks/{id} (204); uno que ya no existe (404) cuenta como borrado.</summary>
    public async Task<Result> DeleteWebhookAsync(
        TenantProviderCredentials credentials,
        string providerWebhookId,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(credentials.PublishableKey))
            return Result.Failure(
                new Error("PayPal.MissingClientId", "PayPal requires a client-id (publishable key).")
            );

        var baseUrl = ResolveBaseUrl(credentials.ApiBaseUrl);
        var tokenResult = await GetAccessTokenAsync(baseUrl, credentials, ct);
        if (tokenResult.IsFailure)
            return Result.Failure(tokenResult.Error);

        try
        {
            using var http = httpClientFactory.CreateClient();
            using var message = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{baseUrl}/v1/notifications/webhooks/{Uri.EscapeDataString(providerWebhookId)}"
            );
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);
            using var response = await http.SendAsync(message, ct);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                return Result.Success();

            var (code, detail) = ReadPayPalError(await response.Content.ReadAsStringAsync(ct));
            logger.LogWarning("PayPal webhook deletion failed ({Status}): {Code}", (int)response.StatusCode, code);
            return Result.Failure(RegistrationError(response.StatusCode, code, detail));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "PayPal webhook deletion threw.");
            return Result.Failure(new Error(WebhookRegistrationErrors.TransientCode, ex.Message));
        }
    }

    /// <summary>429/5xx = pasajero; el resto (credenciales, URL rechazada, tope de webhooks) no.</summary>
    private static Error RegistrationError(HttpStatusCode status, string? code, string? detail) =>
        (int)status is 429 or >= 500
            ? new Error(WebhookRegistrationErrors.TransientCode, detail ?? $"PayPal answered HTTP {(int)status}.")
            : new Error(
                $"PayPal.Webhook.{code ?? ((int)status).ToString(CultureInfo.InvariantCulture)}",
                detail ?? "PayPal rejected the webhook."
            );

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
                logger.LogWarning(
                    "PayPal refund failed for capture {Reference}: {Code} {Detail}",
                    providerChargeReference,
                    code,
                    detail
                );
                return Result.Failure<RefundResult>(
                    new Error("PayPal.Refund.Failed", detail ?? "PayPal refund failed.")
                );
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
    /// Verificación por postback (la que recomienda PayPal): se le reenvían los encabezados
    /// <c>PAYPAL-*</c>, el Webhook ID de la oficina y el evento tal cual llegó (sin re-serializar) y PayPal
    /// responde <c>verification_status</c>. Solo <c>SUCCESS</c> cuenta como válido; si PayPal no responde
    /// se devuelve fallo para que el endpoint conteste no 2xx y PayPal reintente.
    /// </summary>
    public async Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        TenantProviderCredentials credentials,
        CancellationToken ct
    )
    {
        var authAlgo = headers.Find(AuthAlgoHeader);
        var certUrl = headers.Find(CertUrlHeader);
        var transmissionId = headers.Find(TransmissionIdHeader);
        var transmissionSig = headers.Find(TransmissionSigHeader);
        var transmissionTime = headers.Find(TransmissionTimeHeader);
        if (
            string.IsNullOrWhiteSpace(authAlgo)
            || string.IsNullOrWhiteSpace(certUrl)
            || string.IsNullOrWhiteSpace(transmissionId)
            || string.IsNullOrWhiteSpace(transmissionSig)
            || string.IsNullOrWhiteSpace(transmissionTime)
        )
            return Result.Failure<WebhookVerificationResult>(
                new Error("PayPal.WebhookHeaders.Missing", "One or more PayPal webhook signature headers are missing.")
            );

        if (string.IsNullOrWhiteSpace(credentials.WebhookSecret))
            return Result.Failure<WebhookVerificationResult>(
                new Error("PayPal.Webhook.WebhookIdMissing", "The PayPal Webhook ID is not configured.")
            );

        if (string.IsNullOrWhiteSpace(credentials.PublishableKey))
            return Result.Failure<WebhookVerificationResult>(
                new Error("PayPal.MissingClientId", "PayPal requires a client-id (publishable key).")
            );

        string? eventId;
        string? eventType;
        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            eventId = ReadString(doc.RootElement, "id");
            eventType = ReadString(doc.RootElement, "event_type");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "PayPal webhook payload is not valid JSON.");
            return Result.Failure<WebhookVerificationResult>(
                new Error("PayPal.Webhook.ParseFailed", "PayPal webhook payload is not valid JSON.")
            );
        }

        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType))
            return Result.Failure<WebhookVerificationResult>(
                new Error("PayPal.Webhook.ParseFailed", "PayPal webhook payload has no id or event_type.")
            );

        var baseUrl = ResolveBaseUrl(credentials.ApiBaseUrl);
        var tokenResult = await GetAccessTokenAsync(baseUrl, credentials, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<WebhookVerificationResult>(tokenResult.Error);

        try
        {
            using var http = httpClientFactory.CreateClient();
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/v1/notifications/verify-webhook-signature"
            );
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);
            message.Content = new StringContent(
                BuildVerificationBody(
                    authAlgo,
                    certUrl,
                    transmissionId,
                    transmissionSig,
                    transmissionTime,
                    credentials.WebhookSecret,
                    rawPayload
                ),
                Encoding.UTF8,
                "application/json"
            );

            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "PayPal webhook verification returned HTTP {Status} for event {EventId}.",
                    (int)response.StatusCode,
                    eventId
                );
                return Result.Failure<WebhookVerificationResult>(VerificationUnavailable);
            }

            using var doc = JsonDocument.Parse(body);
            var status = ReadString(doc.RootElement, "verification_status");
            if (!string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("PayPal webhook signature rejected ({Status}) for event {EventId}.", status, eventId);
                // Como en Stripe: el detalle queda en el log, no se ecoa al caller anónimo.
                return Result.Failure<WebhookVerificationResult>(
                    new Error("PayPal.WebhookSignature.Invalid", "The webhook signature could not be verified.")
                );
            }

            return Result.Success(new WebhookVerificationResult(eventId, eventType, rawPayload, transmissionSig));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "PayPal webhook verification threw for event {EventId}.", eventId);
            return Result.Failure<WebhookVerificationResult>(VerificationUnavailable);
        }
    }

    private static readonly Error VerificationUnavailable = new(
        "PayPal.WebhookSignature.Unavailable",
        "PayPal could not verify the webhook right now; it will retry."
    );

    // webhook_event va crudo: PayPal pide reenviar el evento exactamente como llegó.
    private static string BuildVerificationBody(
        string authAlgo,
        string certUrl,
        string transmissionId,
        string transmissionSig,
        string transmissionTime,
        string webhookId,
        string rawPayload
    )
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("auth_algo", authAlgo);
            writer.WriteString("cert_url", certUrl);
            writer.WriteString("transmission_id", transmissionId);
            writer.WriteString("transmission_sig", transmissionSig);
            writer.WriteString("transmission_time", transmissionTime);
            writer.WriteString("webhook_id", webhookId);
            writer.WritePropertyName("webhook_event");
            writer.WriteRawValue(rawPayload);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reembolso (<c>PAYMENT.CAPTURE.REFUNDED</c>) → Refunded con el total reembolsado de la captura;
    /// reverso (<c>PAYMENT.CAPTURE.REVERSED</c>, PayPal retira el dinero) y disputa
    /// (<c>CUSTOMER.DISPUTE.CREATED</c>) → ChargedBack. La referencia es el id de la captura, el mismo
    /// que <see cref="AuthorizeChargeAsync"/> guarda como <c>ProviderChargeReference</c>.
    /// </summary>
    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    )
    {
        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            if (
                !doc.RootElement.TryGetProperty("resource", out var resource)
                || resource.ValueKind != JsonValueKind.Object
            )
                return Task.FromResult(UnexpectedPayload("The event has no resource."));

            var resourceType = ReadString(doc.RootElement, "resource_type");
            var result = eventType switch
            {
                CaptureRefunded => ParseRefund(resource, resourceType),
                CaptureReversed => ParseReversal(resource, resourceType),
                DisputeCreated => ParseDispute(resource),
                _ => Result.Failure<WebhookEventPayload>(
                    new Error(WebhookEventPayload.UnsupportedEventTypeCode, $"Event type '{eventType}' is not handled.")
                ),
            };
            return Task.FromResult(result);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "PayPal webhook payload could not be parsed.");
            return Task.FromResult(
                Result.Failure<WebhookEventPayload>(new Error("PayPal.Webhook.ParseFailed", ex.Message))
            );
        }
    }

    // El recurso es un refund (su captura sale de related_ids o del link "up") o, si PayPal manda la captura,
    // la captura misma. El total es acumulado, igual que charge.amount_refunded de Stripe.
    private static Result<WebhookEventPayload> ParseRefund(JsonElement resource, string? resourceType)
    {
        var captureId = ResolveCaptureId(resource, resourceType);
        if (captureId is null)
            return UnexpectedPayload("The refund does not reference a capture.");

        var totalRefunded = IsCapture(resourceType)
            ? ReadAmountCents(resource, "amount")
            : ReadAmountCents(resource, "seller_payable_breakdown", "total_refunded_amount")
                // Sin el acumulado, el monto de este reembolso (cota inferior: nunca suma de más).
                ?? ReadAmountCents(resource, "amount");
        if (totalRefunded is null)
            return UnexpectedPayload("The refund has no amount.");

        return Result.Success(
            new WebhookEventPayload(
                ProviderChargeReference: captureId,
                Status: PaymentStatus.Refunded,
                FailureCode: null,
                FailureMessage: null,
                RefundedAmountCents: totalRefunded
            )
        );
    }

    private static Result<WebhookEventPayload> ParseReversal(JsonElement resource, string? resourceType)
    {
        var captureId = ResolveCaptureId(resource, resourceType);
        if (captureId is null)
            return UnexpectedPayload("The reversal does not reference a capture.");

        return Result.Success(
            new WebhookEventPayload(
                ProviderChargeReference: captureId,
                Status: PaymentStatus.ChargedBack,
                FailureCode: CaptureReversed,
                FailureMessage: "PayPal reversed the capture.",
                RefundedAmountCents: null
            )
        );
    }

    // La disputa apunta a la captura por disputed_transactions[].seller_transaction_id.
    private static Result<WebhookEventPayload> ParseDispute(JsonElement resource)
    {
        string? captureId = null;
        if (
            resource.TryGetProperty("disputed_transactions", out var transactions)
            && transactions.ValueKind == JsonValueKind.Array
        )
            foreach (var transaction in transactions.EnumerateArray())
            {
                captureId = ReadString(transaction, "seller_transaction_id");
                if (!string.IsNullOrWhiteSpace(captureId))
                    break;
            }

        if (string.IsNullOrWhiteSpace(captureId))
            return UnexpectedPayload("The dispute does not reference a transaction.");

        return Result.Success(
            new WebhookEventPayload(
                ProviderChargeReference: captureId,
                Status: PaymentStatus.ChargedBack,
                FailureCode: ReadString(resource, "reason"),
                FailureMessage: "Chargeback dispute created.",
                RefundedAmountCents: null
            )
        );
    }

    private static bool IsCapture(string? resourceType) =>
        string.Equals(resourceType, "capture", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveCaptureId(JsonElement resource, string? resourceType)
    {
        if (IsCapture(resourceType))
            return ReadString(resource, "id");

        if (
            resource.TryGetProperty("supplementary_data", out var supplementary)
            && supplementary.ValueKind == JsonValueKind.Object
            && supplementary.TryGetProperty("related_ids", out var related)
            && ReadString(related, "capture_id") is { Length: > 0 } relatedCaptureId
        )
            return relatedCaptureId;

        // Link "up" del refund: .../v2/payments/captures/{capture_id}
        if (resource.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
            foreach (var link in links.EnumerateArray())
            {
                if (!string.Equals(ReadString(link, "rel"), "up", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Uri.TryCreate(ReadString(link, "href"), UriKind.Absolute, out var href))
                    continue;

                var segments = href.AbsolutePath.Trim('/').Split('/');
                if (segments.Length >= 2 && segments[^2] == "captures" && segments[^1].Length > 0)
                    return Uri.UnescapeDataString(segments[^1]);
            }

        return null;
    }

    private static long? ReadAmountCents(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return null;

        var value = ReadString(current, "value");
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? (long)Math.Round(parsed * 100m, MidpointRounding.AwayFromZero)
            : null;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Result<WebhookEventPayload> UnexpectedPayload(string message) =>
        Result.Failure<WebhookEventPayload>(new Error("PayPal.Webhook.UnexpectedPayload", message));

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
                    new Error(
                        "PayPal.Auth.Failed",
                        "Could not authenticate with PayPal (check client-id/secret and URL)."
                    )
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
