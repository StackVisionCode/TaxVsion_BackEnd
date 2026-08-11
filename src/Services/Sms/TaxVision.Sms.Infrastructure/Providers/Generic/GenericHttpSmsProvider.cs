using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using TaxVision.Sms.Application.Providers;

namespace TaxVision.Sms.Infrastructure.Providers.Generic;

/// <summary>
/// Adapter genérico dirigido por configuración (`Sms:Providers:generic`): sirve para casi cualquier
/// proveedor SMS REST sin código nuevo. Mapea el request canónico a un body JSON/form según `RequestMap`,
/// aplica el auth configurado, hace el POST y extrae el `providerMessageId` de la respuesta. Los webhooks
/// se verifican por HMAC y se parsean por paths + `StatusMap` de la config. Casos raros de un proveedor
/// se resuelven creando OTRO adapter, sin tocar este.
/// </summary>
[SmsProvider("generic")]
public sealed class GenericHttpSmsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SmsProvidersOptions> options,
    HttpResiliencePipelineRegistry resilience,
    ILogger<GenericHttpSmsProvider> logger
) : ISmsProvider
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string Code => "generic";

    private SmsProviderConfig Config =>
        options.Value.Providers.TryGetValue(Code, out var cfg)
            ? cfg
            : throw new InvalidOperationException("Sms:Providers:generic is not configured.");

    public SmsProviderCapabilities Capabilities
    {
        get
        {
            var c = Config.Capabilities;
            return new SmsProviderCapabilities
            {
                SupportsDeliveryReceipts = c.SupportsDeliveryReceipts,
                SupportsInbound = c.SupportsInbound,
                SupportsBulkSend = c.SupportsBulkSend,
                MaxBatchSize = c.MaxBatchSize,
                SupportsMedia = c.SupportsMedia,
                SupportsMultipleMedia = c.SupportsMultipleMedia,
                MaxMediaItems = c.MaxMediaItems,
                MaxMediaSizeBytes = c.MaxMediaSizeBytes,
                AllowedMediaTypes = c.AllowedMediaTypes.ToHashSet(StringComparer.OrdinalIgnoreCase),
            };
        }
    }

    public async Task<Result<SmsSendResult>> SendAsync(SmsSendRequest request, CancellationToken ct = default)
    {
        var config = Config;
        var http = httpClientFactory.CreateClient(nameof(GenericHttpSmsProvider));

        using var httpRequest = new HttpRequestMessage(new HttpMethod(config.HttpMethod), BuildUrl(config))
        {
            Content = BuildContent(config, request),
        };
        ApplyAuth(httpRequest, config.Auth);

        try
        {
            var breaker = resilience.GetOrCreate(nameof(GenericHttpSmsProvider));
            using var response = await breaker.ExecuteAsync(token => http.SendAsync(httpRequest, token), ct);
            var payload = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Generic SMS provider returned {StatusCode} for {To}.", (int)response.StatusCode, request.To);
                return Result.Success(new SmsSendResult(false, null, SmsCanonicalError(response.StatusCode), payload));
            }

            var providerMessageId = ExtractString(payload, config.ResponseMap.ProviderMessageIdPath) ?? Guid.NewGuid().ToString("N");
            return Result.Success(new SmsSendResult(true, providerMessageId, null, null));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(ex, "Generic SMS provider request failed for {To}.", request.To);
            return Result.Success(new SmsSendResult(false, null, "providerUnavailable", "Could not reach the SMS provider."));
        }
    }

    public async Task<Result<IReadOnlyList<SmsSendResult>>> SendBatchAsync(IReadOnlyList<SmsSendRequest> requests, CancellationToken ct = default)
    {
        // Sin bulk nativo configurado: loop por mensaje.
        var results = new List<SmsSendResult>(requests.Count);
        foreach (var r in requests)
            results.Add((await SendAsync(r, ct)).Value);
        return Result.Success<IReadOnlyList<SmsSendResult>>(results);
    }

    public Result<SmsSignatureCheck> VerifySignature(string rawPayload, string signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return Result.Success(new SmsSignatureCheck(false, "No webhook secret configured."));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawPayload)));
        var isValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes((signatureHeader ?? string.Empty).Trim().ToLowerInvariant())
        );
        return Result.Success(new SmsSignatureCheck(isValid, isValid ? null : "Signature mismatch."));
    }

    public Result<SmsDeliveryUpdate> ParseDeliveryReceipt(string rawPayload)
    {
        var w = Config.Webhook;
        var providerMessageId = ExtractString(rawPayload, w.ProviderMessageIdPath);
        var rawStatus = ExtractString(rawPayload, w.StatusPath);
        if (string.IsNullOrWhiteSpace(providerMessageId) || string.IsNullOrWhiteSpace(rawStatus))
            return Result.Failure<SmsDeliveryUpdate>(new Error("sms.webhook.malformed", "Malformed DLR payload."));

        var canonical = w.StatusMap.TryGetValue(rawStatus, out var mapped) ? mapped : rawStatus;
        var status = canonical.ToLowerInvariant() switch
        {
            "delivered" => SmsCanonicalStatus.Delivered,
            "failed" => SmsCanonicalStatus.Failed,
            "undeliverable" => SmsCanonicalStatus.Undeliverable,
            _ => SmsCanonicalStatus.Accepted,
        };
        var errorCode = w.ErrorCodePath is null ? null : ExtractString(rawPayload, w.ErrorCodePath);
        var eventType = ExtractString(rawPayload, w.EventTypePath) ?? rawStatus;
        return Result.Success(new SmsDeliveryUpdate(providerMessageId!, eventType, status, errorCode, null));
    }

    public Result<SmsInboundMessage> ParseInbound(string rawPayload)
    {
        var w = Config.Webhook;
        var from = ExtractString(rawPayload, w.FromPath);
        var text = ExtractString(rawPayload, w.KeywordPath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(from))
            return Result.Failure<SmsInboundMessage>(new Error("sms.webhook.malformed", "Malformed inbound payload."));

        var keyword = text.Trim().ToUpperInvariant() switch
        {
            "STOP" => SmsInboundKeyword.Stop,
            "START" => SmsInboundKeyword.Start,
            "HELP" => SmsInboundKeyword.Help,
            _ => SmsInboundKeyword.Unknown,
        };
        var providerMessageId = ExtractString(rawPayload, w.ProviderMessageIdPath) ?? Guid.NewGuid().ToString("N");
        return Result.Success(new SmsInboundMessage(from!, keyword, text.Trim(), "inbound", providerMessageId, null, null));
    }

    private static string BuildUrl(SmsProviderConfig config) =>
        config.BaseUrl.TrimEnd('/') + "/" + config.SendPath.TrimStart('/');

    private static HttpContent BuildContent(SmsProviderConfig config, SmsSendRequest request)
    {
        var map = config.RequestMap;
        if (string.Equals(config.BodyFormat, "form", StringComparison.OrdinalIgnoreCase))
        {
            var form = new List<KeyValuePair<string, string>>
            {
                new(map.To, request.To),
                new(map.From, config.SenderId ?? string.Empty),
                new(map.Body, request.Body),
            };
            if (request.Media.Count > 0)
                form.Add(new(map.Media, request.Media[0].Url));
            return new FormUrlEncodedContent(form);
        }

        var json = new Dictionary<string, object?>
        {
            [map.To] = request.To,
            [map.From] = config.SenderId,
            [map.Body] = request.Body,
        };
        if (request.Media.Count > 0)
            json[map.Media] = request.Media.Select(m => m.Url).ToArray();
        return new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json");
    }

    private static void ApplyAuth(HttpRequestMessage request, SmsAuthConfig auth)
    {
        switch (auth.Type.ToLowerInvariant())
        {
            case "bearer":
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Credential);
                break;
            case "basic":
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(auth.Credential ?? string.Empty))
                );
                break;
            case "apikeyheader":
                if (!string.IsNullOrWhiteSpace(auth.HeaderName))
                    request.Headers.TryAddWithoutValidation(auth.HeaderName, auth.Credential);
                break;
        }
    }

    /// <summary>Extrae un string por path con puntos (`a.b.c`) del JSON. Null si no existe.</summary>
    private static string? ExtractString(string payload, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var element = doc.RootElement;
            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out var next))
                    return null;
                element = next;
            }
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                _ => element.GetRawText(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SmsCanonicalError(System.Net.HttpStatusCode status) =>
        (int)status >= 500 ? "providerUnavailable" : "providerRejected";
}
