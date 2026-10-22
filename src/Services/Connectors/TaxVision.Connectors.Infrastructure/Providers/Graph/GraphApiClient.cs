using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Observability;

namespace TaxVision.Connectors.Infrastructure.Providers.Graph;

/// <summary>
/// Client de Microsoft Graph — Inbox-only forzado (D1, §34.5): el delta query siempre apunta a
/// <c>/me/mailFolders/inbox/messages/delta</c>, nunca <c>/me/messages/delta</c> (mailbox completo).
/// El cursor (deltaLink/nextLink) es una URL opaca completa que Graph devuelve — se persiste tal
/// cual, sin reconstruirla. Toda llamada pasa por un <see cref="HttpResiliencePipeline"/> propio
/// (F24, clave <c>"Graph:messages"</c>) que reintenta fallos de red transitorios y abre tras
/// fallos consecutivos.
/// </summary>
public sealed class GraphApiClient(
    HttpClient httpClient,
    IOAuthTokenManager tokenManager,
    IProviderRateLimiter rateLimiter,
    HttpResiliencePipelineRegistry circuitBreakers,
    ILogger<GraphApiClient> logger
) : IEmailProviderClient, IOutboundEmailProviderClient
{
    private const string BaseUrl = "https://graph.microsoft.com/v1.0/me";

    /// <summary>D3 Compose §9/§11.2 — sendMail/reply son atómicos (sin id navegable para adjuntar después vía upload session), así que el total de adjuntos queda topeado acá en vez de migrar a createDraft→createUploadSession→send (fuera de alcance v1).</summary>
    private const long GraphAttachmentSizeLimitBytes = 3 * 1024 * 1024;
    private const string InboxDeltaBaseUrl =
        BaseUrl
        + "/mailFolders/inbox/messages/delta?$select=id,conversationId,subject,from,toRecipients,ccRecipients,bccRecipients,receivedDateTime,hasAttachments,internetMessageId,internetMessageHeaders,bodyPreview";
    private const int MaxDeltaPages = 10;

    private static readonly JsonSerializerOptions SendRequestJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ProviderCode ProviderCode => ProviderCode.Graph;

    public async Task<HistoryPage> GetHistoryAsync(
        Guid accountId,
        Guid tenantId,
        string? sinceCursor,
        CancellationToken ct = default
    )
    {
        var messageIds = new List<string>();
        var url = sinceCursor ?? InboxDeltaBaseUrl;
        string? finalDeltaLink = null;
        var hasMore = false;

        for (var page = 0; page < MaxDeltaPages; page++)
        {
            var payload = await SendCoreAsync<GraphDeltaResponse>(accountId, tenantId, url, ct, goneAsNull: true);
            if (payload is null)
            {
                // 410: el estado del delta venció en Microsoft. Se reinicia desde cero; Correspondence
                // descarta los que ya tenía por InternetMessageId.
                logger.LogWarning("Graph delta state expired for account {AccountId}; restarting delta.", accountId);
                url = InboxDeltaBaseUrl;
                sinceCursor = null;
                continue;
            }

            // Los borrados (@removed) no son correo nuevo.
            foreach (var item in payload.Value ?? [])
            {
                if (item.Removed is null)
                    messageIds.Add(item.Id);
            }

            if (!string.IsNullOrEmpty(payload.DeltaLink))
            {
                finalDeltaLink = payload.DeltaLink;
                break;
            }

            if (string.IsNullOrEmpty(payload.NextLink))
                break;

            url = payload.NextLink;
            hasMore = page == MaxDeltaPages - 1;
        }

        // Sin deltaLink porque se llegó al tope: se guarda el nextLink para seguir desde ahí. Guardar el
        // cursor viejo hacía releer las mismas 10 páginas en cada pasada sin avanzar nunca.
        var nextCursor = finalDeltaLink ?? (hasMore ? url : sinceCursor);
        return new HistoryPage(messageIds, nextCursor, hasMore);
    }

    public async Task<RawMessage> GetMessageAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        CancellationToken ct = default
    )
    {
        var url =
            $"{BaseUrl}/messages/{providerMessageId}"
            + "?$select=id,conversationId,subject,from,toRecipients,ccRecipients,bccRecipients,receivedDateTime,hasAttachments,internetMessageId,internetMessageHeaders,bodyPreview";
        var message = await SendAsync<GraphMessage>(accountId, tenantId, url, ct);

        var attachments =
            message.HasAttachments == true
                ? await GetAttachmentMetadataAsync(accountId, tenantId, providerMessageId, ct)
                : [];

        string? Header(string name) =>
            (message.InternetMessageHeaders ?? [])
                .FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))
                ?.Value;

        var references = (Header("References") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new RawMessage(
            message.Id,
            message.ConversationId,
            message.InternetMessageId ?? Header("Message-ID"),
            Header("In-Reply-To"),
            references,
            message.From?.EmailAddress?.Address ?? string.Empty,
            NullIfBlank(message.From?.EmailAddress?.Name),
            ExtractAddresses(message.ToRecipients),
            ExtractAddresses(message.CcRecipients),
            ExtractAddresses(message.BccRecipients),
            message.Subject ?? string.Empty,
            message.BodyPreview ?? string.Empty,
            message.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,
            attachments,
            AuthenticationResultsHeaderParser.Parse(Header("Authentication-Results"))
        );
    }

    /// <summary>
    /// Graph devuelve UNA sola representación del body (contentType "html" o "text", nunca ambos —
    /// a diferencia del árbol MIME multipart/alternative de Gmail). El tamaño MIME es una
    /// aproximación (bytes del contenido devuelto) — Graph no expone el tamaño crudo sin pedir
    /// <c>/messages/{id}/$value</c> (fuera de alcance acá).
    /// </summary>
    public async Task<byte[]> GetRawMessageAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        CancellationToken ct = default
    )
    {
        var url = $"{BaseUrl}/messages/{providerMessageId}/$value";
        var response = await GetAsync(accountId, tenantId, url, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Graph API request to {Url} returned HTTP {Status}.", url, (int)response.StatusCode);
            throw new EmailProviderException($"Graph API returned HTTP {(int)response.StatusCode}.");
        }
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<MessageBody> GetMessageBodyAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        CancellationToken ct = default
    )
    {
        var url = $"{BaseUrl}/messages/{providerMessageId}?$select=id,body,internetMessageHeaders,hasAttachments";
        var message = await SendAsync<GraphFullMessage>(accountId, tenantId, url, ct);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in message.InternetMessageHeaders ?? [])
            headers.TryAdd(header.Name, header.Value);

        var attachments =
            message.HasAttachments == true
                ? await GetAttachmentMetadataAsync(accountId, tenantId, providerMessageId, ct)
                : [];

        var isHtml = string.Equals(message.Body?.ContentType, "html", StringComparison.OrdinalIgnoreCase);
        var content = message.Body?.Content;
        var mimeSize = content is null ? 0 : Encoding.UTF8.GetByteCount(content);

        return new MessageBody(mimeSize, isHtml ? content : null, isHtml ? null : content, headers, attachments);
    }

    public async Task<Stream> GetAttachmentAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        string attachmentId,
        CancellationToken ct = default
    )
    {
        var url = $"{BaseUrl}/messages/{providerMessageId}/attachments/{attachmentId}";
        var payload = await SendAsync<GraphAttachment>(accountId, tenantId, url, ct);
        var bytes = Convert.FromBase64String(payload.ContentBytes ?? string.Empty);
        return new MemoryStream(bytes, writable: false);
    }

    private async Task<IReadOnlyList<RawMessageAttachment>> GetAttachmentMetadataAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        CancellationToken ct
    )
    {
        var url = $"{BaseUrl}/messages/{providerMessageId}/attachments?$select=id,name,contentType,size";
        var payload = await SendAsync<GraphAttachmentListResponse>(accountId, tenantId, url, ct);
        return (payload.Value ?? [])
            .Select(a => new RawMessageAttachment(
                a.Id,
                a.Name ?? "attachment",
                a.ContentType ?? "application/octet-stream",
                a.Size
            ))
            .ToList();
    }

    /// <summary>
    /// D3 §3.4/§6.2 — reply de un paso (<c>POST /me/messages/{id}/reply</c>, solo requiere
    /// <c>Mail.Send</c>, Graph arma threading solo) cuando hay <c>ReplyToProviderMessageId</c>, si no
    /// <c>POST /me/sendMail</c>. Ninguno de los dos devuelve el id del mensaje enviado (202 sin
    /// cuerpo, D3 §11 — pendiente documentado) — a diferencia de Gmail. Nunca toca
    /// <c>internetMessageHeaders</c> a mano en el reply: la doc de Microsoft advierte que romper eso
    /// rompe el threading que <c>reply</c> ya arma.
    /// </summary>
    public async Task<SendMessageResult> SendMessageAsync(
        Guid accountId,
        Guid tenantId,
        string fromAddress,
        string? fromDisplayName,
        OutboundMessage message,
        CancellationToken ct = default
    )
    {
        // Los inline cuentan para el limite: viajan en el mismo array.
        var totalAttachmentBytes =
            message.Attachments.Sum(a => (long)a.Content.Length)
            + message.InlineAssets.Sum(a => (long)a.Content.Length);
        if (totalAttachmentBytes > GraphAttachmentSizeLimitBytes)
        {
            ConnectorsMetrics.SendFailures.Add(
                1,
                new KeyValuePair<string, object?>("provider", "Graph"),
                new KeyValuePair<string, object?>("reason", SendFailureReason.AttachmentTooLarge.ToString())
            );
            throw new OutboundEmailSendException(
                SendFailureReason.AttachmentTooLarge,
                $"Total attachment size {totalAttachmentBytes} bytes exceeds the Graph sendMail/reply limit of {GraphAttachmentSizeLimitBytes} bytes."
            );
        }

        if (!string.IsNullOrWhiteSpace(message.ReplyToProviderMessageId))
        {
            var (url, requestBody, extraHeaders) = BuildReplyRequest(message);
            await EnsureSentAsync(await SendWriteRequestAsync(accountId, tenantId, url, requestBody, extraHeaders, ct));
            ConnectorsMetrics.MessagesSent.Add(1, new KeyValuePair<string, object?>("provider", "Graph"));
            // El reply de un paso devuelve 202 sin cuerpo: no hay id que leer.
            return new SendMessageResult(ProviderMessageId: null, ProviderThreadId: null, DateTime.UtcNow);
        }

        return await SendNewThroughDraftAsync(accountId, tenantId, message, ct);
    }

    /// <summary>
    /// sendMail no devuelve nada. Crear el borrador sí devuelve su internetMessageId, y es el que van a
    /// citar los avisos de no entrega. Después se envía ese borrador.
    /// </summary>
    private async Task<SendMessageResult> SendNewThroughDraftAsync(
        Guid accountId,
        Guid tenantId,
        OutboundMessage message,
        CancellationToken ct
    )
    {
        var draftResponse = await SendWriteRequestAsync(
            accountId,
            tenantId,
            $"{BaseUrl}/messages",
            JsonSerializer.Serialize(BuildDraftMessage(message), SendRequestJsonOptions),
            null,
            ct
        );
        await EnsureSentAsync(draftResponse);
        var draft = await draftResponse.Content.ReadFromJsonAsync<GraphCreatedMessage>(cancellationToken: ct);
        if (draft is null || string.IsNullOrWhiteSpace(draft.Id))
        {
            RecordSendFailureMetric(SendFailureReason.TransientProviderError);
            throw new OutboundEmailSendException(
                SendFailureReason.TransientProviderError,
                "Graph API returned no draft id."
            );
        }

        var sendResponse = await SendWriteRequestAsync(
            accountId,
            tenantId,
            $"{BaseUrl}/messages/{draft.Id}/send",
            string.Empty,
            null,
            ct
        );
        if (!sendResponse.IsSuccessStatusCode)
            // El borrador queda en Drafts: el usuario lo ve y puede reenviarlo a mano.
            logger.LogWarning("Graph draft {DraftId} was created but could not be sent.", draft.Id);
        await EnsureSentAsync(sendResponse);

        ConnectorsMetrics.MessagesSent.Add(1, new KeyValuePair<string, object?>("provider", "Graph"));
        return new SendMessageResult(draft.Id, draft.ConversationId, DateTime.UtcNow, draft.InternetMessageId);
    }

    private Task EnsureSentAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return Task.CompletedTask;

        var exception = BuildSendException(response);
        RecordSendFailureMetric(exception.Reason);
        throw exception;
    }

    private static void RecordSendFailureMetric(SendFailureReason reason) =>
        ConnectorsMetrics.SendFailures.Add(
            1,
            new KeyValuePair<string, object?>("provider", "Graph"),
            new KeyValuePair<string, object?>("reason", reason.ToString())
        );

    private static GraphSendMailMessage BuildDraftMessage(OutboundMessage message) =>
        new(
            message.Subject,
            new GraphItemBodyRequest("html", message.Html),
            ToRecipients(message.To),
            ToRecipients(message.Cc),
            ToRecipients(message.Bcc),
            string.IsNullOrWhiteSpace(message.ReplyToDisplayAddress)
                ? null
                : ToRecipients([message.ReplyToDisplayAddress]),
            ToAttachments(message)
        );

    private static (string Url, string Body, IReadOnlyDictionary<string, string>? Headers) BuildReplyRequest(
        OutboundMessage message
    )
    {
        var payload = new GraphReplyRequest(
            message.Html,
            new GraphReplyMessageOverride(
                ToRecipients(message.To),
                ToRecipients(message.Cc),
                ToRecipients(message.Bcc),
                ToAttachments(message)
            )
        );
        // Sin este header, Graph trata "comment" como texto plano (default documentado) — message.Html
        // necesita renderizarse como HTML.
        var headers = new Dictionary<string, string> { ["Prefer"] = "outlook.body-content-type=\"html\"" };
        return (
            $"{BaseUrl}/messages/{message.ReplyToProviderMessageId}/reply",
            JsonSerializer.Serialize(payload, SendRequestJsonOptions),
            headers
        );
    }

    /// <summary>Graph no arma MIME: adjuntos e inline van en el mismo array y los separa
    /// <c>isInline</c> + <c>contentId</c>.</summary>
    private static List<GraphFileAttachmentRequest>? ToAttachments(OutboundMessage message)
    {
        if (message.Attachments.Count == 0 && message.InlineAssets.Count == 0)
            return null;

        var all = message
            .Attachments.Select(a => new GraphFileAttachmentRequest(
                a.Filename,
                a.ContentType,
                Convert.ToBase64String(a.Content)
            ))
            .ToList();

        all.AddRange(
            message.InlineAssets.Select(a => new GraphFileAttachmentRequest(
                a.ContentId,
                a.ContentType,
                Convert.ToBase64String(a.Content),
                IsInline: true,
                ContentId: a.ContentId
            ))
        );

        return all;
    }

    private static List<GraphRecipientRequest>? ToRecipients(IReadOnlyList<string> addresses) =>
        addresses.Count == 0
            ? null
            : addresses.Select(a => new GraphRecipientRequest(new GraphEmailAddressRequest(a))).ToList();

    private static OutboundEmailSendException BuildSendException(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var category = status switch
        {
            401 => SendFailureReason.AuthExpired,
            403 => SendFailureReason.PermissionDenied,
            429 => SendFailureReason.QuotaExceeded,
            400 => SendFailureReason.InvalidRequest,
            >= 500 => SendFailureReason.TransientProviderError,
            _ => SendFailureReason.TransientProviderError,
        };
        return new OutboundEmailSendException(category, $"Graph send returned HTTP {status}.");
    }

    /// <summary>Mismo pipeline rate-limit+token+breaker que <see cref="SendAsync{T}"/>, pero para el path de envío — lanza <see cref="OutboundEmailSendException"/> en vez de <see cref="EmailProviderException"/> (D3 §8).</summary>
    private async Task<HttpResponseMessage> SendWriteRequestAsync(
        Guid accountId,
        Guid tenantId,
        string url,
        string jsonBody,
        IReadOnlyDictionary<string, string>? extraHeaders,
        CancellationToken ct
    )
    {
        await rateLimiter.WaitForSlotAsync(ProviderCode, tenantId, ct);

        var tokenResult = await tokenManager.GetValidAccessTokenAsync(accountId, ct);
        if (tokenResult.IsFailure)
            throw new OutboundEmailSendException(
                SendFailureReason.AuthExpired,
                $"Could not obtain a valid access token: {tokenResult.Error.Message}"
            );

        var breaker = circuitBreakers.GetOrCreate("Graph:messages");
        try
        {
            return await breaker.ExecuteAsync(
                token =>
                    SendWithRetryAfterAsync(url, HttpMethod.Post, jsonBody, extraHeaders, tokenResult.Value, token),
                ct
            );
        }
        catch (BrokenCircuitException ex)
        {
            throw new OutboundEmailSendException(
                SendFailureReason.TransientProviderError,
                "Graph API circuit breaker is open — too many recent failures.",
                ex
            );
        }
        catch (HttpRequestException ex)
        {
            throw new OutboundEmailSendException(
                SendFailureReason.TransientProviderError,
                "Graph API request failed (network error) after retries.",
                ex
            );
        }
    }

    private async Task<T> SendAsync<T>(Guid accountId, Guid tenantId, string url, CancellationToken ct)
        where T : class =>
        // Sin goneAsNull un 410 lanza, así que nunca vuelve null.
        (await SendCoreAsync<T>(accountId, tenantId, url, ct, goneAsNull: false))!;

    private async Task<T?> SendCoreAsync<T>(
        Guid accountId,
        Guid tenantId,
        string url,
        CancellationToken ct,
        bool goneAsNull
    )
        where T : class
    {
        var response = await GetAsync(accountId, tenantId, url, ct);

        if (goneAsNull && response.StatusCode == HttpStatusCode.Gone)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Graph API request to {Url} returned HTTP {Status}.", url, (int)response.StatusCode);
            throw new EmailProviderException($"Graph API returned HTTP {(int)response.StatusCode}.");
        }

        var parsed = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return parsed ?? throw new EmailProviderException("Graph API returned an empty response body.");
    }

    /// <summary>GET autenticado con cupo, token y breaker; el caller interpreta la respuesta.</summary>
    private async Task<HttpResponseMessage> GetAsync(Guid accountId, Guid tenantId, string url, CancellationToken ct)
    {
        await rateLimiter.WaitForSlotAsync(ProviderCode, tenantId, ct);

        var tokenResult = await tokenManager.GetValidAccessTokenAsync(accountId, ct);
        if (tokenResult.IsFailure)
            throw new EmailProviderException(
                $"Could not obtain a valid access token: {tokenResult.Error.Message}",
                requiresReconnect: OAuthTokenErrorCodes.RequiresReconnect(tokenResult.Error.Code)
            );

        var breaker = circuitBreakers.GetOrCreate("Graph:messages");
        try
        {
            return await breaker.ExecuteAsync(
                token => SendWithRetryAfterAsync(url, HttpMethod.Get, null, null, tokenResult.Value, token),
                ct
            );
        }
        catch (BrokenCircuitException ex)
        {
            throw new EmailProviderException("Graph API circuit breaker is open — too many recent failures.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new EmailProviderException("Graph API request failed (network error) after retries.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAfterAsync(
        string url,
        HttpMethod method,
        string? jsonBody,
        IReadOnlyDictionary<string, string>? extraHeaders,
        string accessToken,
        CancellationToken ct
    )
    {
        var response = await SendOnceAsync(url, method, jsonBody, extraHeaders, accessToken, ct);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
            return response;

        var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
        await rateLimiter.RecordRateLimitedAsync(ProviderCode, retryAfter, ct);
        logger.LogWarning("Graph API rate limited — waiting {RetryAfter} before retrying {Url}.", retryAfter, url);
        await Task.Delay(retryAfter, ct);

        return await SendOnceAsync(url, method, jsonBody, extraHeaders, accessToken, ct);
    }

    /// <summary>
    /// Deja propagar <see cref="HttpRequestException"/> sin envolver — el pipeline Polly del circuit
    /// breaker (Fase 10) la reintenta antes de que <see cref="SendAsync{T}"/>/<see cref="SendWriteRequestAsync"/>
    /// la conviertan en su excepción respectiva. Arma un <see cref="StringContent"/> nuevo en cada
    /// llamada — <c>HttpRequestMessage.Dispose()</c> dispone su <c>Content</c>, reusar la misma
    /// instancia entre el intento original y el retry de 429 rompería el segundo intento.
    /// </summary>
    private async Task<HttpResponseMessage> SendOnceAsync(
        string url,
        HttpMethod method,
        string? jsonBody,
        IReadOnlyDictionary<string, string>? extraHeaders,
        string accessToken,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (extraHeaders is not null)
        {
            foreach (var header in extraHeaders)
                request.Headers.Add(header.Key, header.Value);
        }
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return await httpClient.SendAsync(request, ct);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IReadOnlyList<string> ExtractAddresses(List<GraphRecipient>? recipients) =>
        (recipients ?? []).Select(r => r.EmailAddress?.Address ?? string.Empty).Where(a => a.Length > 0).ToList();

    private sealed record GraphDeltaResponse
    {
        [JsonPropertyName("value")]
        public List<GraphMessageRef>? Value { get; init; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }

        [JsonPropertyName("@odata.deltaLink")]
        public string? DeltaLink { get; init; }
    }

    private sealed record GraphMessageRef
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("@removed")]
        public JsonElement? Removed { get; init; }
    }

    private sealed record GraphMessage
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("conversationId")]
        public string? ConversationId { get; init; }

        [JsonPropertyName("subject")]
        public string? Subject { get; init; }

        [JsonPropertyName("from")]
        public GraphRecipient? From { get; init; }

        [JsonPropertyName("toRecipients")]
        public List<GraphRecipient>? ToRecipients { get; init; }

        [JsonPropertyName("ccRecipients")]
        public List<GraphRecipient>? CcRecipients { get; init; }

        [JsonPropertyName("bccRecipients")]
        public List<GraphRecipient>? BccRecipients { get; init; }

        [JsonPropertyName("receivedDateTime")]
        public DateTimeOffset? ReceivedDateTime { get; init; }

        [JsonPropertyName("hasAttachments")]
        public bool? HasAttachments { get; init; }

        [JsonPropertyName("internetMessageId")]
        public string? InternetMessageId { get; init; }

        [JsonPropertyName("internetMessageHeaders")]
        public List<GraphInternetMessageHeader>? InternetMessageHeaders { get; init; }

        [JsonPropertyName("bodyPreview")]
        public string? BodyPreview { get; init; }
    }

    private sealed record GraphRecipient
    {
        [JsonPropertyName("emailAddress")]
        public GraphEmailAddress? EmailAddress { get; init; }
    }

    private sealed record GraphEmailAddress
    {
        [JsonPropertyName("address")]
        public string? Address { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed record GraphInternetMessageHeader
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; init; } = string.Empty;
    }

    private sealed record GraphAttachmentListResponse
    {
        [JsonPropertyName("value")]
        public List<GraphAttachmentMetadata>? Value { get; init; }
    }

    private sealed record GraphAttachmentMetadata
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("contentType")]
        public string? ContentType { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }
    }

    private sealed record GraphAttachment
    {
        [JsonPropertyName("contentBytes")]
        public string? ContentBytes { get; init; }
    }

    private sealed record GraphFullMessage
    {
        [JsonPropertyName("body")]
        public GraphItemBody? Body { get; init; }

        [JsonPropertyName("internetMessageHeaders")]
        public List<GraphInternetMessageHeader>? InternetMessageHeaders { get; init; }

        [JsonPropertyName("hasAttachments")]
        public bool? HasAttachments { get; init; }
    }

    private sealed record GraphItemBody
    {
        [JsonPropertyName("contentType")]
        public string? ContentType { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed record GraphCreatedMessage
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("internetMessageId")]
        public string? InternetMessageId { get; init; }

        [JsonPropertyName("conversationId")]
        public string? ConversationId { get; init; }
    }

    private sealed record GraphSendMailMessage(
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("body")] GraphItemBodyRequest Body,
        [property: JsonPropertyName("toRecipients")] List<GraphRecipientRequest>? ToRecipients,
        [property: JsonPropertyName("ccRecipients")] List<GraphRecipientRequest>? CcRecipients,
        [property: JsonPropertyName("bccRecipients")] List<GraphRecipientRequest>? BccRecipients,
        [property: JsonPropertyName("replyTo")] List<GraphRecipientRequest>? ReplyTo,
        [property: JsonPropertyName("attachments")] List<GraphFileAttachmentRequest>? Attachments
    );

    private sealed record GraphReplyRequest(
        [property: JsonPropertyName("comment")] string Comment,
        [property: JsonPropertyName("message")] GraphReplyMessageOverride Message
    );

    private sealed record GraphReplyMessageOverride(
        [property: JsonPropertyName("toRecipients")] List<GraphRecipientRequest>? ToRecipients,
        [property: JsonPropertyName("ccRecipients")] List<GraphRecipientRequest>? CcRecipients,
        [property: JsonPropertyName("bccRecipients")] List<GraphRecipientRequest>? BccRecipients,
        [property: JsonPropertyName("attachments")] List<GraphFileAttachmentRequest>? Attachments
    );

    private sealed record GraphFileAttachmentRequest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("contentType")] string ContentType,
        [property: JsonPropertyName("contentBytes")] string ContentBytes,
        [property: JsonPropertyName("isInline")] bool? IsInline = null,
        [property: JsonPropertyName("contentId")] string? ContentId = null
    )
    {
        [JsonPropertyName("@odata.type")]
        public string ODataType => "#microsoft.graph.fileAttachment";
    }

    private sealed record GraphItemBodyRequest(
        [property: JsonPropertyName("contentType")] string ContentType,
        [property: JsonPropertyName("content")] string Content
    );

    private sealed record GraphRecipientRequest(
        [property: JsonPropertyName("emailAddress")] GraphEmailAddressRequest EmailAddress
    );

    private sealed record GraphEmailAddressRequest([property: JsonPropertyName("address")] string Address);
}
