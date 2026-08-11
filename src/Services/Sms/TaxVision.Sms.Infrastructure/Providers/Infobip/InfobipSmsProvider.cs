using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using TaxVision.Sms.Application.Providers;

namespace TaxVision.Sms.Infrastructure.Providers.Infobip;

/// <summary>
/// Adapter del proveedor <b>Infobip</b> (https://www.infobip.com/docs/api). El body/response de Infobip
/// son ANIDADOS (<c>messages[].destinations[].to</c>, <c>messages[].status.groupName</c>), forma que el
/// adapter genérico plano no puede expresar — por eso es un adapter dedicado (el caso "otro adapter"
/// que la arquitectura contempla).
/// <list type="bullet">
///   <item>Auth: header propio de Infobip <c>Authorization: App {apiKey}</c> (NO Basic/Bearer). La
///   API key es SECRETA — viene por env/secret-manager en <see cref="SmsAuthConfig.Credential"/>, nunca
///   hardcoded.</item>
///   <item>Envío: <c>POST {BaseUrl}/sms/2/text/advanced</c> con
///   <c>{ messages:[{ destinations:[{to}], from, text }] }</c>; se lee <c>messages[0].messageId</c> y
///   <c>messages[0].status.groupName</c> (REJECTED ⇒ no aceptado).</item>
///   <item>DLR: Infobip empuja reportes de entrega a un <c>notifyUrl</c> (webhook) con
///   <c>results[].status.groupName</c> (DELIVERED/UNDELIVERABLE/REJECTED/EXPIRED/PENDING).</item>
///   <item>Inbound (MO): <c>results[].from</c> + <c>results[].text</c> (STOP/START/HELP).</item>
/// </list>
/// <c>BaseUrl</c> (ej. <c>https://vyg8je.api.infobip.com</c>) y <c>SenderId</c> se configuran en
/// <c>Sms:Providers:infobip</c>. La firma de webhook de Infobip debe confirmarse contra la cuenta; acá
/// se valida por HMAC si se configura <c>Webhook.Secret</c>, fail-closed si no.
/// </summary>
[SmsProvider(ProviderCode)]
public sealed class InfobipSmsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SmsProvidersOptions> options,
    HttpResiliencePipelineRegistry resilience,
    ILogger<InfobipSmsProvider> logger
) : ISmsProvider
{
    public const string ProviderCode = "infobip";
    private const string DefaultSendPath = "/sms/2/text/advanced";

    // Encoder relajado: emite '+' del E.164 literal (no +) para un body limpio y legible.
    private static readonly JsonSerializerOptions BodyJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string Code => ProviderCode;

    private SmsProviderConfig Config =>
        options.Value.Providers.TryGetValue(ProviderCode, out var cfg)
            ? cfg
            : throw new InvalidOperationException($"Sms:Providers:{ProviderCode} is not configured.");

    /// <summary>Capacidades reales de Infobip para SMS de texto: DLR y MO por webhook, bulk nativo,
    /// sin media en este endpoint (MMS es un flujo aparte). Fijas en código, no configurables.</summary>
    public SmsProviderCapabilities Capabilities { get; } =
        new()
        {
            SupportsDeliveryReceipts = true,
            SupportsInbound = true,
            SupportsBulkSend = true,
            MaxBatchSize = 1000,
            SupportsMedia = false,
            SupportsMultipleMedia = false,
            MaxMediaItems = 0,
            MaxMediaSizeBytes = 0,
            AllowedMediaTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

    public async Task<Result<SmsSendResult>> SendAsync(SmsSendRequest request, CancellationToken ct = default)
    {
        var config = Config;
        var http = httpClientFactory.CreateClient(nameof(InfobipSmsProvider));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl(config))
        {
            Content = BuildBody(config, request.To, request.Body),
        };
        ApplyInfobipAuth(httpRequest, config.Auth.Credential);

        try
        {
            var breaker = resilience.GetOrCreate(nameof(InfobipSmsProvider));
            using var response = await breaker.ExecuteAsync(token => http.SendAsync(httpRequest, token), ct);
            var payload = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Infobip returned {StatusCode} for {To}.", (int)response.StatusCode, request.To);
                return Result.Success(new SmsSendResult(false, null, CanonicalError(response.StatusCode), payload));
            }

            var (parsed, messageId, groupName) = ParseSendResponse(payload);
            if (!parsed)
                return Result.Success(new SmsSendResult(false, null, "providerRejected", payload));

            var rejected = string.Equals(groupName, "REJECTED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(groupName, "UNDELIVERABLE", StringComparison.OrdinalIgnoreCase);
            if (rejected)
                return Result.Success(new SmsSendResult(false, messageId, "providerRejected", groupName));

            return Result.Success(new SmsSendResult(true, messageId ?? Guid.NewGuid().ToString("N"), null, null));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(ex, "Infobip request failed for {To}.", request.To);
            return Result.Success(new SmsSendResult(false, null, "providerUnavailable", "Could not reach Infobip."));
        }
    }

    public async Task<Result<IReadOnlyList<SmsSendResult>>> SendBatchAsync(
        IReadOnlyList<SmsSendRequest> requests,
        CancellationToken ct = default
    )
    {
        // Infobip acepta múltiples messages en una llamada; por simplicidad y para no perder el
        // mapeo 1:1 request↔resultado, se envía por mensaje (idéntico contrato que el resto).
        var results = new List<SmsSendResult>(requests.Count);
        foreach (var r in requests)
            results.Add((await SendAsync(r, ct)).Value);
        return Result.Success<IReadOnlyList<SmsSendResult>>(results);
    }

    public Result<SmsSignatureCheck> VerifySignature(string rawPayload, string signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return Result.Success(new SmsSignatureCheck(false, "No webhook secret configured for Infobip."));

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
        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            if (!TryFirst(doc.RootElement, "results", out var r))
                return Result.Failure<SmsDeliveryUpdate>(new Error("sms.webhook.malformed", "Malformed DLR payload."));

            var messageId = GetString(r, "messageId");
            var groupName = r.TryGetProperty("status", out var st) ? GetString(st, "groupName") : null;
            if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(groupName))
                return Result.Failure<SmsDeliveryUpdate>(new Error("sms.webhook.malformed", "Malformed DLR payload."));

            var status = groupName!.ToUpperInvariant() switch
            {
                "DELIVERED" => SmsCanonicalStatus.Delivered,
                "UNDELIVERABLE" => SmsCanonicalStatus.Undeliverable,
                "REJECTED" or "EXPIRED" => SmsCanonicalStatus.Failed,
                _ => SmsCanonicalStatus.Accepted, // PENDING / en tránsito
            };
            var eventType = (r.TryGetProperty("status", out var st2) ? GetString(st2, "name") : null) ?? groupName!;
            var failureCode = status is SmsCanonicalStatus.Failed or SmsCanonicalStatus.Undeliverable ? groupName : null;
            return Result.Success(new SmsDeliveryUpdate(messageId!, eventType, status, failureCode, null));
        }
        catch (JsonException)
        {
            return Result.Failure<SmsDeliveryUpdate>(new Error("sms.webhook.malformed", "Unparseable DLR payload."));
        }
    }

    public Result<SmsInboundMessage> ParseInbound(string rawPayload)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            if (!TryFirst(doc.RootElement, "results", out var r))
                return Result.Failure<SmsInboundMessage>(new Error("sms.webhook.malformed", "Malformed inbound payload."));

            var from = GetString(r, "from");
            var text = GetString(r, "text") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(from))
                return Result.Failure<SmsInboundMessage>(new Error("sms.webhook.malformed", "Malformed inbound payload."));

            var keyword = text.Trim().ToUpperInvariant() switch
            {
                "STOP" => SmsInboundKeyword.Stop,
                "START" => SmsInboundKeyword.Start,
                "HELP" => SmsInboundKeyword.Help,
                _ => SmsInboundKeyword.Unknown,
            };
            var messageId = GetString(r, "messageId") ?? Guid.NewGuid().ToString("N");
            return Result.Success(new SmsInboundMessage(from!, keyword, text.Trim(), "inbound", messageId, null, null));
        }
        catch (JsonException)
        {
            return Result.Failure<SmsInboundMessage>(new Error("sms.webhook.malformed", "Unparseable inbound payload."));
        }
    }

    private static string BuildUrl(SmsProviderConfig config)
    {
        var path = string.IsNullOrWhiteSpace(config.SendPath) || config.SendPath == "/" ? DefaultSendPath : config.SendPath;
        return config.BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private static StringContent BuildBody(SmsProviderConfig config, string to, string text)
    {
        var body = new
        {
            messages = new[]
            {
                new
                {
                    destinations = new[] { new { to } },
                    from = config.SenderId,
                    text,
                },
            },
        };
        return new StringContent(JsonSerializer.Serialize(body, BodyJson), Encoding.UTF8, "application/json");
    }

    /// <summary>Infobip usa el esquema propio <c>Authorization: App {apiKey}</c>.</summary>
    private static void ApplyInfobipAuth(HttpRequestMessage request, string? apiKey) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("App", apiKey ?? string.Empty);

    private static (bool ok, string? messageId, string? groupName) ParseSendResponse(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!TryFirst(doc.RootElement, "messages", out var m))
                return (false, null, null);
            var messageId = GetString(m, "messageId");
            var groupName = m.TryGetProperty("status", out var st) ? GetString(st, "groupName") : null;
            return (true, messageId, groupName);
        }
        catch (JsonException)
        {
            return (false, null, null);
        }
    }

    private static bool TryFirst(JsonElement root, string arrayName, out JsonElement first)
    {
        first = default;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(arrayName, out var arr)
            || arr.ValueKind != JsonValueKind.Array
            || arr.GetArrayLength() == 0)
            return false;
        first = arr[0];
        return true;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string CanonicalError(System.Net.HttpStatusCode status) =>
        (int)status >= 500 ? "providerUnavailable" : "providerRejected";
}
