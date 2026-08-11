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

namespace TaxVision.Sms.Infrastructure.Providers.Twilio;

/// <summary>
/// Adapter de <b>Twilio</b> (https://www.twilio.com/docs). Particularidades vs los otros adapters:
/// <list type="bullet">
///   <item>Auth HTTP Basic con <c>base64(AccountSid:AuthToken)</c>. Se toma
///   <see cref="SmsAuthConfig.Credential"/> como el par ya unido <c>"AccountSid:AuthToken"</c>
///   (secreto por env). El AccountSid también arma la URL <c>/2010-04-01/Accounts/{sid}/Messages.json</c>.</item>
///   <item>Body de envío <b>form-urlencoded</b> (<c>To</c>, <c>From</c>, <c>Body</c>, y <c>MediaUrl</c>
///   repetido para MMS). Respuesta JSON: <c>sid</c>, <c>status</c>, <c>error_code</c>.</item>
///   <item>Soporta <b>MMS</b> (media) — a diferencia de Infobip/Textmaxx.</item>
///   <item>DLR e inbound llegan como <b>form-urlencoded</b> (<c>MessageSid</c>/<c>MessageStatus</c>;
///   <c>From</c>/<c>Body</c>) al StatusCallback / número.</item>
///   <item>Firma de webhook <b>X-Twilio-Signature</b>: <c>base64(HMAC-SHA1(authToken, url + params ordenados))</c>
///   — por eso necesita la URL pública del request (la pasa el controller).</item>
/// </list>
/// Config en <c>Sms:Providers:twilio</c>: <c>BaseUrl</c> (default api.twilio.com), <c>SenderId</c> (el número
/// From o un Messaging Service Sid), <c>Auth.Credential="AccountSid:AuthToken"</c> por env.
/// </summary>
[SmsProvider(ProviderCode)]
public sealed class TwilioSmsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SmsProvidersOptions> options,
    HttpResiliencePipelineRegistry resilience,
    ILogger<TwilioSmsProvider> logger
) : ISmsProvider
{
    public const string ProviderCode = "twilio";
    private const string DefaultBaseUrl = "https://api.twilio.com";

    public string Code => ProviderCode;

    private SmsProviderConfig Config =>
        options.Value.Providers.TryGetValue(ProviderCode, out var cfg)
            ? cfg
            : throw new InvalidOperationException($"Sms:Providers:{ProviderCode} is not configured.");

    /// <summary>Capacidades reales de Twilio: DLR (StatusCallback) + inbound (MO), MMS (media), sin bulk nativo.</summary>
    public SmsProviderCapabilities Capabilities { get; } =
        new()
        {
            SupportsDeliveryReceipts = true,
            SupportsInbound = true,
            SupportsBulkSend = false,
            MaxBatchSize = 1,
            SupportsMedia = true,
            SupportsMultipleMedia = true,
            MaxMediaItems = 10,
            MaxMediaSizeBytes = 5_242_880, // ~5 MB por item (límite MMS de Twilio)
            AllowedMediaTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

    public async Task<Result<SmsSendResult>> SendAsync(SmsSendRequest request, CancellationToken ct = default)
    {
        var config = Config;
        var (accountSid, _) = SplitCredential(config.Auth.Credential);
        if (string.IsNullOrWhiteSpace(accountSid))
            return Result.Success(new SmsSendResult(false, null, "providerRejected", "Twilio AccountSid is not configured."));

        var http = httpClientFactory.CreateClient(nameof(TwilioSmsProvider));
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl(config, accountSid))
        {
            Content = BuildForm(config, request),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(config.Auth.Credential ?? string.Empty))
        );

        try
        {
            var breaker = resilience.GetOrCreate(nameof(TwilioSmsProvider));
            using var response = await breaker.ExecuteAsync(token => http.SendAsync(httpRequest, token), ct);
            var payload = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = ParseError(payload);
                logger.LogWarning("Twilio returned {StatusCode} for {To} (code {Code}).", (int)response.StatusCode, request.To, code);
                return Result.Success(new SmsSendResult(false, null, code ?? CanonicalError(response.StatusCode), message ?? payload));
            }

            var (sid, status, errorCode) = ParseSendResponse(payload);
            var rejected = errorCode is not null
                || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "undelivered", StringComparison.OrdinalIgnoreCase);
            if (rejected)
                return Result.Success(new SmsSendResult(false, sid, errorCode ?? "providerRejected", status));

            return Result.Success(new SmsSendResult(true, sid ?? Guid.NewGuid().ToString("N"), null, null));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(ex, "Twilio request failed for {To}.", request.To);
            return Result.Success(new SmsSendResult(false, null, "providerUnavailable", "Could not reach Twilio."));
        }
    }

    public async Task<Result<IReadOnlyList<SmsSendResult>>> SendBatchAsync(
        IReadOnlyList<SmsSendRequest> requests,
        CancellationToken ct = default
    )
    {
        var results = new List<SmsSendResult>(requests.Count);
        foreach (var r in requests)
            results.Add((await SendAsync(r, ct)).Value);
        return Result.Success<IReadOnlyList<SmsSendResult>>(results);
    }

    /// <summary>Valida la firma real de Twilio: <c>base64(HMAC-SHA1(authToken, url + Σ(key+value) ordenados))</c>.
    /// El authToken sale de la credencial del propio adapter (parte tras ':'), no del webhook secret genérico.</summary>
    public Result<SmsSignatureCheck> VerifySignature(string rawPayload, string signatureHeader, string secret, string requestUrl = "")
    {
        var (_, authToken) = SplitCredential(Config.Auth.Credential);
        if (string.IsNullOrEmpty(authToken))
            return Result.Success(new SmsSignatureCheck(false, "No Twilio auth token configured."));
        if (string.IsNullOrWhiteSpace(requestUrl))
            return Result.Success(new SmsSignatureCheck(false, "Twilio signature validation requires the request URL."));

        var form = ParseForm(rawPayload);
        var data = new StringBuilder(requestUrl);
        foreach (var kv in form.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            data.Append(kv.Key);
            data.Append(kv.Value);
        }

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        var computed = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data.ToString())));
        var isValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(signatureHeader ?? string.Empty)
        );
        return Result.Success(new SmsSignatureCheck(isValid, isValid ? null : "Signature mismatch."));
    }

    public Result<SmsDeliveryUpdate> ParseDeliveryReceipt(string rawPayload)
    {
        var form = ParseForm(rawPayload);
        var messageSid = form.GetValueOrDefault("MessageSid") ?? form.GetValueOrDefault("SmsSid");
        var rawStatus = form.GetValueOrDefault("MessageStatus") ?? form.GetValueOrDefault("SmsStatus");
        if (string.IsNullOrWhiteSpace(messageSid) || string.IsNullOrWhiteSpace(rawStatus))
            return Result.Failure<SmsDeliveryUpdate>(new Error("sms.webhook.malformed", "Malformed Twilio DLR payload."));

        var status = rawStatus!.ToLowerInvariant() switch
        {
            "delivered" => SmsCanonicalStatus.Delivered,
            "undelivered" => SmsCanonicalStatus.Undeliverable,
            "failed" => SmsCanonicalStatus.Failed,
            _ => SmsCanonicalStatus.Accepted, // queued / sending / sent
        };
        var errorCode = form.GetValueOrDefault("ErrorCode");
        return Result.Success(new SmsDeliveryUpdate(messageSid!, rawStatus!, status, errorCode, null));
    }

    public Result<SmsInboundMessage> ParseInbound(string rawPayload)
    {
        var form = ParseForm(rawPayload);
        var from = form.GetValueOrDefault("From");
        var text = form.GetValueOrDefault("Body") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(from))
            return Result.Failure<SmsInboundMessage>(new Error("sms.webhook.malformed", "Malformed Twilio inbound payload."));

        var keyword = text.Trim().ToUpperInvariant() switch
        {
            "STOP" => SmsInboundKeyword.Stop,
            "START" => SmsInboundKeyword.Start,
            "HELP" => SmsInboundKeyword.Help,
            _ => SmsInboundKeyword.Unknown,
        };
        var messageSid = form.GetValueOrDefault("MessageSid") ?? form.GetValueOrDefault("SmsSid") ?? Guid.NewGuid().ToString("N");
        return Result.Success(new SmsInboundMessage(from!, keyword, text.Trim(), "inbound", messageSid, null, null));
    }

    private static string BuildUrl(SmsProviderConfig config, string accountSid)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? DefaultBaseUrl : config.BaseUrl;
        return $"{baseUrl.TrimEnd('/')}/2010-04-01/Accounts/{accountSid}/Messages.json";
    }

    private static FormUrlEncodedContent BuildForm(SmsProviderConfig config, SmsSendRequest request)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("From", config.SenderId ?? string.Empty),
            new("Body", request.Body),
        };
        foreach (var media in request.Media)
            fields.Add(new KeyValuePair<string, string>("MediaUrl", media.Url)); // MMS: MediaUrl repetido
        return new FormUrlEncodedContent(fields);
    }

    private static (string sid, string token) SplitCredential(string? credential)
    {
        var value = credential ?? string.Empty;
        var idx = value.IndexOf(':');
        return idx < 0 ? (value, string.Empty) : (value[..idx], value[(idx + 1)..]);
    }

    private static (string? sid, string? status, string? errorCode) ParseSendResponse(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var sid = GetString(root, "sid");
            var status = GetString(root, "status");
            var errorCode = root.TryGetProperty("error_code", out var ec) && ec.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? ec.ToString()
                : null;
            return (sid, status, errorCode);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static (string? code, string? message) ParseError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var c) && c.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? c.ToString()
                : null;
            return (code, GetString(root, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Parsea un body <c>application/x-www-form-urlencoded</c> a pares clave→valor decodificados.</summary>
    private static Dictionary<string, string> ParseForm(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(body))
            return result;
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var key = idx < 0 ? pair : pair[..idx];
            var value = idx < 0 ? string.Empty : pair[(idx + 1)..];
            result[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return result;
    }

    private static string CanonicalError(System.Net.HttpStatusCode status) =>
        (int)status >= 500 ? "providerUnavailable" : "providerRejected";
}
