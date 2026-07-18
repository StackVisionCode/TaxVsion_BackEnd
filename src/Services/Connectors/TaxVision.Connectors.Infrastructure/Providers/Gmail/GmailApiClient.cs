using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;
using Polly.CircuitBreaker;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Observability;
using TaxVision.Connectors.Infrastructure.RateLimit;

namespace TaxVision.Connectors.Infrastructure.Providers.Gmail;

/// <summary>
/// Client de Gmail API — Inbox-only forzado (D1, §34.5): history y lookups siempre filtran
/// <c>labelId=INBOX</c>, nunca sincroniza el mailbox completo aunque la API lo permita por default.
/// Nunca pide <c>format=full</c> acá — eso es Fase 8 (body fetch bajo demanda). Toda llamada pasa por
/// un <see cref="ProviderCircuitBreaker"/> propio (Fase 10, clave <c>"Gmail:messages"</c> — separado del
/// breaker de OAuth refresh) que reintenta fallos de red transitorios y abre tras fallos consecutivos.
/// </summary>
public sealed class GmailApiClient(
    HttpClient httpClient,
    IOAuthTokenManager tokenManager,
    IProviderRateLimiter rateLimiter,
    ProviderCircuitBreakerRegistry circuitBreakers,
    ILogger<GmailApiClient> logger
) : IEmailProviderClient, IOutboundEmailProviderClient
{
    private const string BaseUrl = "https://gmail.googleapis.com/gmail/v1/users/me";
    private const int MaxHistoryPages = 10;

    private static readonly JsonSerializerOptions SendRequestJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] MetadataHeaders =
    [
        "From",
        "To",
        "Cc",
        "Bcc",
        "Subject",
        "Message-ID",
        "In-Reply-To",
        "References",
        "Authentication-Results",
    ];

    public ProviderCode ProviderCode => ProviderCode.Gmail;

    public async Task<HistoryPage> GetHistoryAsync(Guid accountId, string? sinceCursor, CancellationToken ct = default)
    {
        var messageIds = new List<string>();
        var pageToken = (string?)null;
        var latestHistoryId = sinceCursor;
        var hasMore = false;

        for (var page = 0; page < MaxHistoryPages; page++)
        {
            var url =
                $"{BaseUrl}/history?labelId=INBOX&historyTypes=messageAdded"
                + (sinceCursor is not null ? $"&startHistoryId={Uri.EscapeDataString(sinceCursor)}" : string.Empty)
                + (pageToken is not null ? $"&pageToken={Uri.EscapeDataString(pageToken)}" : string.Empty);

            var payload = await SendAsync<GmailHistoryResponse>(accountId, url, ct);
            if (payload.History is not null)
            {
                foreach (var record in payload.History)
                foreach (var added in record.MessagesAdded ?? [])
                {
                    if (added.Message?.LabelIds?.Contains("INBOX") ?? true)
                        messageIds.Add(added.Message!.Id);
                }
            }

            if (payload.HistoryId is not null)
                latestHistoryId = payload.HistoryId;

            if (string.IsNullOrEmpty(payload.NextPageToken))
                break;

            pageToken = payload.NextPageToken;
            hasMore = page == MaxHistoryPages - 1;
        }

        return new HistoryPage(messageIds, latestHistoryId, hasMore);
    }

    public async Task<RawMessage> GetMessageAsync(
        Guid accountId,
        string providerMessageId,
        CancellationToken ct = default
    )
    {
        var headerQuery = string.Join("&", MetadataHeaders.Select(h => $"metadataHeaders={Uri.EscapeDataString(h)}"));
        var url = $"{BaseUrl}/messages/{providerMessageId}?format=metadata&{headerQuery}";
        var payload = await SendAsync<GmailMessageResponse>(accountId, url, ct);

        var headers = payload.Payload?.Headers ?? [];
        string? Header(string name) =>
            headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

        var attachments = (payload.Payload?.Parts ?? [])
            .Where(p => !string.IsNullOrEmpty(p.Filename) && p.Body?.AttachmentId is not null)
            .Select(p => new RawMessageAttachment(
                p.Body!.AttachmentId!,
                p.Filename!,
                p.MimeType ?? "application/octet-stream",
                p.Body.Size
            ))
            .ToList();

        var references = (Header("References") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new RawMessage(
            payload.Id,
            payload.ThreadId,
            Header("Message-ID"),
            Header("In-Reply-To"),
            references,
            Header("From") ?? string.Empty,
            SplitAddresses(Header("To")),
            SplitAddresses(Header("Cc")),
            SplitAddresses(Header("Bcc")),
            Header("Subject") ?? string.Empty,
            payload.Snippet ?? string.Empty,
            ParseInternalDate(payload.InternalDate),
            attachments,
            AuthenticationResultsHeaderParser.Parse(Header("Authentication-Results"))
        );
    }

    /// <summary>Fetch completo (Fase 8) — a diferencia de GetMessageAsync, acá SÍ se pide format=full y se camina el árbol MIME para extraer html/text (nunca bytes de attachments, esos quedan solo como metadata).</summary>
    public async Task<MessageBody> GetMessageBodyAsync(
        Guid accountId,
        string providerMessageId,
        CancellationToken ct = default
    )
    {
        var url = $"{BaseUrl}/messages/{providerMessageId}?format=full";
        var payload = await SendAsync<GmailFullMessageResponse>(accountId, url, ct);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in payload.Payload?.Headers ?? [])
            headers.TryAdd(header.Name, header.Value);

        var accumulator = new BodyAccumulator();
        if (payload.Payload is not null)
            WalkParts(payload.Payload, accumulator);

        return new MessageBody(
            payload.SizeEstimate,
            accumulator.Html,
            accumulator.Text,
            headers,
            accumulator.Attachments
        );
    }

    private static void WalkParts(GmailFullMessagePayload part, BodyAccumulator accumulator)
    {
        var isAttachment = !string.IsNullOrEmpty(part.Filename) && part.Body?.AttachmentId is not null;
        if (isAttachment)
        {
            accumulator.Attachments.Add(
                new RawMessageAttachment(
                    part.Body!.AttachmentId!,
                    part.Filename!,
                    part.MimeType ?? "application/octet-stream",
                    part.Body.Size
                )
            );
        }
        else if (
            string.Equals(part.MimeType, "text/html", StringComparison.OrdinalIgnoreCase) && part.Body?.Data is not null
        )
        {
            accumulator.Html ??= DecodeBase64UrlText(part.Body.Data);
        }
        else if (
            string.Equals(part.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase)
            && part.Body?.Data is not null
        )
        {
            accumulator.Text ??= DecodeBase64UrlText(part.Body.Data);
        }

        foreach (var child in part.Parts ?? [])
            WalkParts(child, accumulator);
    }

    private static string DecodeBase64UrlText(string data) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(Base64UrlToBase64(data)));

    private sealed class BodyAccumulator
    {
        public string? Html;
        public string? Text;
        public List<RawMessageAttachment> Attachments { get; } = [];
    }

    public async Task<Stream> GetAttachmentAsync(
        Guid accountId,
        string providerMessageId,
        string attachmentId,
        CancellationToken ct = default
    )
    {
        var url = $"{BaseUrl}/messages/{providerMessageId}/attachments/{attachmentId}";
        var payload = await SendAsync<GmailAttachmentResponse>(accountId, url, ct);
        var bytes = Convert.FromBase64String(Base64UrlToBase64(payload.Data ?? string.Empty));
        return new MemoryStream(bytes, writable: false);
    }

    /// <summary>D3 §3.4 — arma un MIME real (MimeKit), resuelve threadId cuando es un reply, y comparte el mismo breaker "Gmail:messages" que el read path (mismo cupo real de Google, D3 §2.1).</summary>
    public async Task<SendMessageResult> SendMessageAsync(
        Guid accountId,
        string fromAddress,
        string? fromDisplayName,
        OutboundMessage message,
        CancellationToken ct = default
    )
    {
        var threadId = await ResolveThreadIdAsync(accountId, message.ReplyToProviderMessageId, ct);
        var raw = BuildRawMimeBase64Url(fromAddress, fromDisplayName, message);
        var requestBody = JsonSerializer.Serialize(new GmailSendRequest(raw, threadId), SendRequestJsonOptions);

        var response = await SendWriteRequestAsync(accountId, $"{BaseUrl}/messages/send", requestBody, ct);
        if (!response.IsSuccessStatusCode)
        {
            var exception = await BuildSendExceptionAsync(response, ct);
            RecordSendFailureMetric(exception.Reason);
            throw exception;
        }

        var parsed = await response.Content.ReadFromJsonAsync<GmailSendResponse>(cancellationToken: ct);
        if (parsed is null)
        {
            RecordSendFailureMetric(SendFailureReason.TransientProviderError);
            throw new OutboundEmailSendException(
                SendFailureReason.TransientProviderError,
                "Gmail API returned an empty response body for send."
            );
        }

        ConnectorsMetrics.MessagesSent.Add(1, new KeyValuePair<string, object?>("provider", "Gmail"));
        return new SendMessageResult(parsed.Id, parsed.ThreadId, DateTime.UtcNow);
    }

    private static void RecordSendFailureMetric(SendFailureReason reason) =>
        ConnectorsMetrics.SendFailures.Add(
            1,
            new KeyValuePair<string, object?>("provider", "Gmail"),
            new KeyValuePair<string, object?>("reason", reason.ToString())
        );

    /// <summary>Gmail requiere threadId + headers References/In-Reply-To juntos para threadear (D3 §6.1) — ninguno solo alcanza.</summary>
    private async Task<string?> ResolveThreadIdAsync(
        Guid accountId,
        string? replyToProviderMessageId,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(replyToProviderMessageId))
            return null;

        try
        {
            var original = await GetMessageAsync(accountId, replyToProviderMessageId, ct);
            return original.ProviderThreadId;
        }
        catch (EmailProviderException ex)
        {
            throw new OutboundEmailSendException(
                SendFailureReason.InvalidRequest,
                $"Could not resolve the original message to reply to: {ex.Message}",
                ex
            );
        }
    }

    private static string BuildRawMimeBase64Url(string fromAddress, string? fromDisplayName, OutboundMessage message)
    {
        MimeMessage mime;
        try
        {
            mime = BuildMimeMessage(fromAddress, fromDisplayName, message);
        }
        catch (ParseException ex)
        {
            throw new OutboundEmailSendException(
                SendFailureReason.InvalidRequest,
                $"Invalid email address in outbound message: {ex.Message}",
                ex
            );
        }

        using var stream = new MemoryStream();
        mime.WriteTo(stream);
        return Convert.ToBase64String(stream.ToArray()).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static MimeMessage BuildMimeMessage(string fromAddress, string? fromDisplayName, OutboundMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(fromDisplayName ?? string.Empty, fromAddress));

        var atIndex = fromAddress.IndexOf('@');
        var fromDomain = atIndex >= 0 ? fromAddress[(atIndex + 1)..] : "taxvision.local";
        mime.MessageId = MimeUtils.GenerateMessageId(fromDomain);

        foreach (var to in message.To)
            mime.To.Add(MailboxAddress.Parse(to));
        foreach (var cc in message.Cc)
            mime.Cc.Add(MailboxAddress.Parse(cc));
        foreach (var bcc in message.Bcc)
            mime.Bcc.Add(MailboxAddress.Parse(bcc));
        if (!string.IsNullOrWhiteSpace(message.ReplyToDisplayAddress))
            mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyToDisplayAddress));

        mime.Subject = message.Subject;

        if (!string.IsNullOrWhiteSpace(message.InReplyToInternetMessageId))
            mime.InReplyTo = message.InReplyToInternetMessageId;
        foreach (var reference in message.References ?? [])
            mime.References.Add(reference);

        var bodyBuilder = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text };
        foreach (var attachment in message.Attachments)
            bodyBuilder.Attachments.Add(
                attachment.Filename,
                attachment.Content,
                ContentType.Parse(attachment.ContentType)
            );

        mime.Body = bodyBuilder.ToMessageBody();
        return mime;
    }

    /// <summary>Taxonomía normalizada D3 §8 — el string exacto <c>insufficientPermissions</c> para problemas de scope no está confirmado en la doc canónica de Gmail (nota honesta del research), se trata como PermissionDenied genérico.</summary>
    private static async Task<OutboundEmailSendException> BuildSendExceptionAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        var status = (int)response.StatusCode;
        var reason = await TryReadGmailErrorReasonAsync(response, ct);

        var category = status switch
        {
            401 => SendFailureReason.AuthExpired,
            403 when IsQuotaReason(reason) => SendFailureReason.QuotaExceeded,
            403 => SendFailureReason.PermissionDenied,
            429 => SendFailureReason.QuotaExceeded,
            400 => SendFailureReason.InvalidRequest,
            >= 500 => SendFailureReason.TransientProviderError,
            _ => SendFailureReason.TransientProviderError,
        };

        return new OutboundEmailSendException(
            category,
            $"Gmail send returned HTTP {status}" + (reason is null ? "." : $" ({reason}).")
        );
    }

    private static bool IsQuotaReason(string? reason) =>
        reason is "dailyLimitExceeded" or "userRateLimitExceeded" or "rateLimitExceeded";

    private static async Task<string?> TryReadGmailErrorReasonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<GmailErrorResponse>(cancellationToken: ct);
            return payload?.Error?.Errors?.FirstOrDefault()?.Reason;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<T> SendAsync<T>(Guid accountId, string url, CancellationToken ct)
    {
        await rateLimiter.WaitForSlotAsync(ProviderCode, ct);

        var tokenResult = await tokenManager.GetValidAccessTokenAsync(accountId, ct);
        if (tokenResult.IsFailure)
            throw new EmailProviderException($"Could not obtain a valid access token: {tokenResult.Error.Message}");

        var breaker = circuitBreakers.GetOrCreate("Gmail:messages");
        HttpResponseMessage response;
        try
        {
            response = await breaker.ExecuteAsync(
                token => SendWithRetryAfterAsync(url, HttpMethod.Get, null, tokenResult.Value, token),
                ct
            );
        }
        catch (BrokenCircuitException ex)
        {
            throw new EmailProviderException("Gmail API circuit breaker is open — too many recent failures.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new EmailProviderException("Gmail API request failed (network error) after retries.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gmail API request to {Url} returned HTTP {Status}.", url, (int)response.StatusCode);
            throw new EmailProviderException($"Gmail API returned HTTP {(int)response.StatusCode}.");
        }

        var parsed = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return parsed ?? throw new EmailProviderException("Gmail API returned an empty response body.");
    }

    /// <summary>Mismo pipeline rate-limit+token+breaker que <see cref="SendAsync{T}"/>, pero para el path de envío — lanza <see cref="OutboundEmailSendException"/> en vez de <see cref="EmailProviderException"/> (D3 §8, el caller necesita saber la razón normalizada, no solo que algo falló).</summary>
    private async Task<HttpResponseMessage> SendWriteRequestAsync(
        Guid accountId,
        string url,
        string jsonBody,
        CancellationToken ct
    )
    {
        await rateLimiter.WaitForSlotAsync(ProviderCode, ct);

        var tokenResult = await tokenManager.GetValidAccessTokenAsync(accountId, ct);
        if (tokenResult.IsFailure)
            throw new OutboundEmailSendException(
                SendFailureReason.AuthExpired,
                $"Could not obtain a valid access token: {tokenResult.Error.Message}"
            );

        var breaker = circuitBreakers.GetOrCreate("Gmail:messages");
        try
        {
            return await breaker.ExecuteAsync(
                token => SendWithRetryAfterAsync(url, HttpMethod.Post, jsonBody, tokenResult.Value, token),
                ct
            );
        }
        catch (BrokenCircuitException ex)
        {
            throw new OutboundEmailSendException(
                SendFailureReason.TransientProviderError,
                "Gmail API circuit breaker is open — too many recent failures.",
                ex
            );
        }
        catch (HttpRequestException ex)
        {
            throw new OutboundEmailSendException(
                SendFailureReason.TransientProviderError,
                "Gmail API request failed (network error) after retries.",
                ex
            );
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAfterAsync(
        string url,
        HttpMethod method,
        string? jsonBody,
        string accessToken,
        CancellationToken ct
    )
    {
        var response = await SendOnceAsync(url, method, jsonBody, accessToken, ct);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return response;

        var retryAfter = ParseRetryAfter(response) ?? TimeSpan.FromSeconds(5);
        await rateLimiter.RecordRateLimitedAsync(ProviderCode, retryAfter, ct);
        logger.LogWarning("Gmail API rate limited — waiting {RetryAfter} before retrying {Url}.", retryAfter, url);
        await Task.Delay(retryAfter, ct);

        return await SendOnceAsync(url, method, jsonBody, accessToken, ct);
    }

    /// <summary>
    /// Deja propagar <see cref="HttpRequestException"/> sin envolver — el pipeline Polly del circuit
    /// breaker (Fase 10) la reintenta antes de que <see cref="SendAsync{T}"/>/<see cref="SendWriteRequestAsync"/>
    /// la conviertan en su excepción respectiva. Arma un <see cref="StringContent"/> nuevo en cada
    /// llamada (nunca reusa la instancia) — <c>HttpRequestMessage.Dispose()</c> dispone su <c>Content</c>,
    /// así que reusar el mismo content entre el intento original y el retry de 429 rompería el segundo intento.
    /// </summary>
    private async Task<HttpResponseMessage> SendOnceAsync(
        string url,
        HttpMethod method,
        string? jsonBody,
        string accessToken,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await httpClient.SendAsync(request, ct);
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
            return null;
        if (header.Delta is { } delta)
            return delta;
        if (header.Date is { } date)
            return date - DateTimeOffset.UtcNow;
        return null;
    }

    private static IReadOnlyList<string> SplitAddresses(string? headerValue) =>
        string.IsNullOrWhiteSpace(headerValue)
            ? []
            : headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static DateTime ParseInternalDate(string? internalDateMillis) =>
        long.TryParse(internalDateMillis, out var millis)
            ? DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime
            : DateTime.UtcNow;

    private static string Base64UrlToBase64(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        return base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
    }

    private sealed record GmailHistoryResponse
    {
        [JsonPropertyName("history")]
        public List<GmailHistoryRecord>? History { get; init; }

        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; init; }

        [JsonPropertyName("historyId")]
        public string? HistoryId { get; init; }
    }

    private sealed record GmailHistoryRecord
    {
        [JsonPropertyName("messagesAdded")]
        public List<GmailMessageAdded>? MessagesAdded { get; init; }
    }

    private sealed record GmailMessageAdded
    {
        [JsonPropertyName("message")]
        public GmailHistoryMessageRef? Message { get; init; }
    }

    private sealed record GmailHistoryMessageRef
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("labelIds")]
        public List<string>? LabelIds { get; init; }
    }

    private sealed record GmailMessageResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("threadId")]
        public string? ThreadId { get; init; }

        [JsonPropertyName("snippet")]
        public string? Snippet { get; init; }

        [JsonPropertyName("internalDate")]
        public string? InternalDate { get; init; }

        [JsonPropertyName("payload")]
        public GmailMessagePayload? Payload { get; init; }
    }

    private sealed record GmailMessagePayload
    {
        [JsonPropertyName("headers")]
        public List<GmailHeader>? Headers { get; init; }

        [JsonPropertyName("parts")]
        public List<GmailMessagePart>? Parts { get; init; }
    }

    private sealed record GmailHeader
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; init; } = string.Empty;
    }

    private sealed record GmailMessagePart
    {
        [JsonPropertyName("filename")]
        public string? Filename { get; init; }

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; init; }

        [JsonPropertyName("body")]
        public GmailMessagePartBody? Body { get; init; }
    }

    private sealed record GmailMessagePartBody
    {
        [JsonPropertyName("attachmentId")]
        public string? AttachmentId { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }
    }

    private sealed record GmailAttachmentResponse
    {
        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("data")]
        public string? Data { get; init; }
    }

    private sealed record GmailFullMessageResponse
    {
        [JsonPropertyName("sizeEstimate")]
        public long SizeEstimate { get; init; }

        [JsonPropertyName("payload")]
        public GmailFullMessagePayload? Payload { get; init; }
    }

    /// <summary>Auto-referenciado: <c>Parts</c> puede anidar multipart/alternative dentro de multipart/mixed arbitrariamente.</summary>
    private sealed record GmailFullMessagePayload
    {
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; init; }

        [JsonPropertyName("filename")]
        public string? Filename { get; init; }

        [JsonPropertyName("headers")]
        public List<GmailHeader>? Headers { get; init; }

        [JsonPropertyName("body")]
        public GmailFullMessagePartBody? Body { get; init; }

        [JsonPropertyName("parts")]
        public List<GmailFullMessagePayload>? Parts { get; init; }
    }

    private sealed record GmailFullMessagePartBody
    {
        [JsonPropertyName("attachmentId")]
        public string? AttachmentId { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("data")]
        public string? Data { get; init; }
    }

    private sealed record GmailSendRequest(
        [property: JsonPropertyName("raw")] string Raw,
        [property: JsonPropertyName("threadId")] string? ThreadId
    );

    private sealed record GmailSendResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("threadId")]
        public string? ThreadId { get; init; }
    }

    private sealed record GmailErrorResponse
    {
        [JsonPropertyName("error")]
        public GmailErrorDetail? Error { get; init; }
    }

    private sealed record GmailErrorDetail
    {
        [JsonPropertyName("errors")]
        public List<GmailErrorItem>? Errors { get; init; }
    }

    private sealed record GmailErrorItem
    {
        [JsonPropertyName("reason")]
        public string? Reason { get; init; }
    }
}
