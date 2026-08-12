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

namespace TaxVision.Sms.Infrastructure.Providers.Textmaxx;

/// <summary>
/// Adapter del proveedor regional <b>Textmaxx</b> (el gateway SMS del CRM legado — ver
/// <c>documents/architecture/campaigns/sms/ADR.md</c> §SMS-ADR-001). Ejemplo de la promesa agnóstica:
/// agregar un proveedor = UNA clase con <see cref="SmsProviderAttribute"/>, sin tocar factory, dominio
/// ni handlers. A diferencia del genérico, este adapter FIJA en código las restricciones reales de
/// Textmaxx para que no puedan declararse mal por config:
/// <list type="bullet">
///   <item>Auth HTTP Basic con <c>base64(clientApiKey:userApiToken)</c> (esquema del legado
///   <c>TextmaxxService.cs:585-591</c>). Se toma <see cref="SmsAuthConfig.Credential"/> como el par
///   ya unido <c>"clientApiKey:userApiToken"</c> (el secreto real viene por env/secret-manager).</item>
///   <item>Solo texto — NO soporta media/MMS.</item>
///   <item>Sin webhook de estado estándar (el legado solo exponía <c>GET /messages/{phone}</c>); por
///   eso <see cref="SmsProviderCapabilities.SupportsDeliveryReceipts"/> es <c>false</c>. El parse de DLR
///   igual está implementado (config-driven) por si se pone un proxy firmante delante — pero la
///   capability por defecto refleja la realidad del proveedor.</item>
/// </list>
/// El endpoint concreto (<c>BaseUrl</c>/<c>SendPath</c>), el formato del body y los nombres de campo
/// (<c>RequestMap</c>/<c>ResponseMap</c>) se parametrizan en <c>Sms:Providers:textmaxx</c> y deben
/// confirmarse contra la API real de Textmaxx antes de producción.
/// </summary>
[SmsProvider(ProviderCode)]
public sealed class TextmaxxSmsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SmsProvidersOptions> options,
    HttpResiliencePipelineRegistry resilience,
    ILogger<TextmaxxSmsProvider> logger
) : ISmsProvider
{
    public const string ProviderCode = "textmaxx";

    public string Code => ProviderCode;

    private SmsProviderConfig Config =>
        options.Value.Providers.TryGetValue(ProviderCode, out var cfg)
            ? cfg
            : throw new InvalidOperationException($"Sms:Providers:{ProviderCode} is not configured.");

    /// <summary>Capacidades FIJAS de Textmaxx (no configurables): solo texto, sin DLR estándar,
    /// inbound sí (STOP/START/HELP), sin bulk nativo.</summary>
    public SmsProviderCapabilities Capabilities { get; } =
        new()
        {
            SupportsDeliveryReceipts = false,
            SupportsInbound = true,
            SupportsBulkSend = false,
            MaxBatchSize = 1,
            SupportsMedia = false,
            SupportsMultipleMedia = false,
            MaxMediaItems = 0,
            MaxMediaSizeBytes = 0,
            AllowedMediaTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

    public async Task<Result<SmsSendResult>> SendAsync(SmsSendRequest request, CancellationToken ct = default)
    {
        var config = Config;
        var http = httpClientFactory.CreateClient(nameof(TextmaxxSmsProvider));

        using var httpRequest = new HttpRequestMessage(new HttpMethod(config.HttpMethod), BuildUrl(config))
        {
            Content = BuildContent(config, request),
        };
        ApplyBasicAuth(httpRequest, config.Auth.Credential);

        try
        {
            var breaker = resilience.GetOrCreate(nameof(TextmaxxSmsProvider));
            using var response = await breaker.ExecuteAsync(token => http.SendAsync(httpRequest, token), ct);
            var payload = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Textmaxx returned {StatusCode} for {To}.", (int)response.StatusCode, request.To);
                return Result.Success(new SmsSendResult(false, null, CanonicalError(response.StatusCode), payload));
            }

            var providerMessageId =
                ExtractString(payload, config.ResponseMap.ProviderMessageIdPath) ?? Guid.NewGuid().ToString("N");
            return Result.Success(new SmsSendResult(true, providerMessageId, null, null));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(ex, "Textmaxx request failed for {To}.", request.To);
            return Result.Success(new SmsSendResult(false, null, "providerUnavailable", "Could not reach Textmaxx."));
        }
    }

    public async Task<Result<IReadOnlyList<SmsSendResult>>> SendBatchAsync(
        IReadOnlyList<SmsSendRequest> requests,
        CancellationToken ct = default
    )
    {
        // Textmaxx no tiene bulk nativo: loop por mensaje (misma semántica que el genérico).
        var results = new List<SmsSendResult>(requests.Count);
        foreach (var r in requests)
            results.Add((await SendAsync(r, ct)).Value);
        return Result.Success<IReadOnlyList<SmsSendResult>>(results);
    }

    public Result<SmsSignatureCheck> VerifySignature(string rawPayload, string signatureHeader, string secret, string requestUrl = "")
    {
        // Textmaxx legado NO firmaba webhooks. Si se pone un proxy firmante delante y se configura
        // Webhook.Secret, se valida por HMAC-SHA256; sin secreto, se rechaza (fail-closed).
        if (string.IsNullOrEmpty(secret))
            return Result.Success(new SmsSignatureCheck(false, "No webhook secret configured for Textmaxx."));

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
        var eventType = ExtractString(rawPayload, w.EventTypePath) ?? rawStatus;
        var errorCode = w.ErrorCodePath is null ? null : ExtractString(rawPayload, w.ErrorCodePath);
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
            return new FormUrlEncodedContent(form);
        }

        var json = new Dictionary<string, object?>
        {
            [map.To] = request.To,
            [map.From] = config.SenderId,
            [map.Body] = request.Body,
        };
        return new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json");
    }

    /// <summary>HTTP Basic con <c>base64(credential)</c>, donde <paramref name="credential"/> es el par
    /// <c>"clientApiKey:userApiToken"</c> de Textmaxx (esquema del legado).</summary>
    private static void ApplyBasicAuth(HttpRequestMessage request, string? credential) =>
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credential ?? string.Empty))
        );

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
                _ => element.GetRawText(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CanonicalError(System.Net.HttpStatusCode status) =>
        (int)status >= 500 ? "providerUnavailable" : "providerRejected";
}
