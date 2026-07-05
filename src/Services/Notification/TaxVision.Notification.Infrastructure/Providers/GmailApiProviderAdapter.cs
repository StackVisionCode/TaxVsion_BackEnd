using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Infrastructure.Providers;

/// <summary>
/// Adaptador Gmail API real vía HTTP. Usa el access token OAuth de la cuenta (refrescado si hace falta).
/// Lista labels como carpetas y sincroniza mensajes (headers, snippet, cuerpo HTML/texto, adjuntos);
/// cursor por internalDate.
/// </summary>
public sealed class GmailApiProviderAdapter(
    OAuthTokenService tokens,
    IHttpClientFactory httpClientFactory,
    ILogger<GmailApiProviderAdapter> logger
) : IEmailProviderAdapter
{
    private const string GmailBase = "https://gmail.googleapis.com/gmail/v1/users/me";
    private const long MaxAttachmentBytes = 26_214_400;

    public EmailExternalProvider Provider => EmailExternalProvider.GmailApi;

    public async Task<Result<IReadOnlyList<ProviderFolder>>> ListFoldersAsync(EmailAccountConnection account, CancellationToken ct = default)
    {
        var client = await CreateClientAsync(account, ct);
        if (client is null)
            return Result.Failure<IReadOnlyList<ProviderFolder>>(NoToken);

        try
        {
            using var doc = await GetJsonAsync(client, $"{GmailBase}/labels", ct);
            var folders = new List<ProviderFolder>();
            if (doc.RootElement.TryGetProperty("labels", out var labels))
                foreach (var l in labels.EnumerateArray())
                {
                    var id = Str(l, "id");
                    var name = Str(l, "name") ?? id ?? "label";
                    if (id is not null)
                        folders.Add(new ProviderFolder(id, name, MapKind(id)));
                }
            return Result.Success<IReadOnlyList<ProviderFolder>>(folders);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gmail list folders failed for account {AccountId}.", account.Id);
            return Result.Failure<IReadOnlyList<ProviderFolder>>(new Error("EmailAccount.SyncFailed", ex.Message));
        }
    }

    public async Task<Result<ProviderFolderSync>> SyncFolderAsync(
        EmailAccountConnection account,
        ProviderFolder folder,
        string? cursor,
        bool full,
        int maxMessages,
        CancellationToken ct = default
    )
    {
        var client = await CreateClientAsync(account, ct);
        if (client is null)
            return Result.Failure<ProviderFolderSync>(NoToken);

        try
        {
            var listUrl = $"{GmailBase}/messages?labelIds={Uri.EscapeDataString(folder.ExternalId)}&maxResults={maxMessages}";
            if (!full && long.TryParse(cursor, out var lastMs))
                listUrl += $"&q={Uri.EscapeDataString($"after:{lastMs / 1000}")}";

            using var list = await GetJsonAsync(client, listUrl, ct);
            var messages = new List<ProviderMessage>();
            var maxInternal = long.TryParse(cursor, out var c) ? c : 0;

            if (list.RootElement.TryGetProperty("messages", out var ids))
                foreach (var idEl in ids.EnumerateArray())
                {
                    var id = Str(idEl, "id");
                    if (id is null)
                        continue;

                    using var msg = await GetJsonAsync(client, $"{GmailBase}/messages/{id}?format=full", ct);
                    var (pm, internalMs) = await MapMessageAsync(client, id, msg.RootElement, ct);
                    messages.Add(pm);
                    if (internalMs > maxInternal)
                        maxInternal = internalMs;
                }

            return Result.Success(new ProviderFolderSync(messages, maxInternal.ToString(), messages.Count));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gmail sync folder {Folder} failed for account {AccountId}.", folder.Name, account.Id);
            return Result.Failure<ProviderFolderSync>(new Error("EmailAccount.SyncFailed", ex.Message));
        }
    }

    private async Task<(ProviderMessage Message, long InternalMs)> MapMessageAsync(HttpClient client, string id, JsonElement msg, CancellationToken ct)
    {
        var threadId = Str(msg, "threadId");
        var snippet = Str(msg, "snippet");
        var internalMs = long.TryParse(Str(msg, "internalDate"), out var ms) ? ms : 0;

        var labels = new HashSet<string>(StringComparer.Ordinal);
        if (msg.TryGetProperty("labelIds", out var lids))
            foreach (var l in lids.EnumerateArray())
                if (l.GetString() is { } s)
                    labels.Add(s);

        string? subject = null, from = null, date = null;
        var to = new List<string>();
        var cc = new List<string>();
        var bcc = new List<string>();
        if (msg.TryGetProperty("payload", out var payload) && payload.TryGetProperty("headers", out var headers))
            foreach (var h in headers.EnumerateArray())
            {
                var name = Str(h, "name")?.ToLowerInvariant();
                var value = Str(h, "value");
                switch (name)
                {
                    case "subject": subject = value; break;
                    case "from": from = value; break;
                    case "to": to.AddRange(SplitAddresses(value)); break;
                    case "cc": cc.AddRange(SplitAddresses(value)); break;
                    case "bcc": bcc.AddRange(SplitAddresses(value)); break;
                    case "date": date = value; break;
                }
            }

        string? html = null, text = null;
        var attachments = new List<ProviderAttachment>();
        if (payload.ValueKind == JsonValueKind.Object)
            WalkPart(payload, ref html, ref text, attachments);

        var materialized = new List<ProviderAttachment>();
        foreach (var att in attachments)
        {
            byte[]? bytes = null;
            if (att.SizeBytes <= MaxAttachmentBytes && att.ExternalId is { Length: > 0 } attId)
                bytes = await FetchAttachmentAsync(client, id, attId, ct);
            materialized.Add(att with { Content = bytes });
        }

        var received = internalMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(internalMs).UtcDateTime : (DateTime?)null;
        var sent = DateTime.TryParse(date, out var sentDt) ? sentDt.ToUniversalTime() : (DateTime?)null;

        var message = new ProviderMessage(
            id, threadId, subject, ExtractEmail(from),
            to, cc, bcc, snippet, html, text, null,
            !labels.Contains("UNREAD"), labels.Contains("STARRED"),
            received, sent, materialized
        );
        return (message, internalMs);
    }

    private static void WalkPart(JsonElement part, ref string? html, ref string? text, List<ProviderAttachment> attachments)
    {
        var mime = Str(part, "mimeType");
        var filename = Str(part, "filename");

        if (part.TryGetProperty("body", out var body))
        {
            var attachmentId = Str(body, "attachmentId");
            if (!string.IsNullOrEmpty(filename) && !string.IsNullOrEmpty(attachmentId))
            {
                var size = body.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s) ? s : 0;
                attachments.Add(new ProviderAttachment(filename, mime, size, attachmentId));
            }
            else if (Str(body, "data") is { } data)
            {
                if (mime == "text/html" && html is null)
                    html = DecodeBase64Url(data);
                else if (mime == "text/plain" && text is null)
                    text = DecodeBase64Url(data);
            }
        }

        if (part.TryGetProperty("parts", out var parts))
            foreach (var p in parts.EnumerateArray())
                WalkPart(p, ref html, ref text, attachments);
    }

    private async Task<byte[]?> FetchAttachmentAsync(HttpClient client, string messageId, string attachmentId, CancellationToken ct)
    {
        try
        {
            using var doc = await GetJsonAsync(client, $"{GmailBase}/messages/{messageId}/attachments/{attachmentId}", ct);
            return Str(doc.RootElement, "data") is { } data ? DecodeBase64UrlBytes(data) : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gmail fetch attachment failed for message {MessageId}.", messageId);
            return null;
        }
    }

    private async Task<HttpClient?> CreateClientAsync(EmailAccountConnection account, CancellationToken ct)
    {
        var token = await tokens.GetValidAccessTokenAsync(account, ct);
        if (string.IsNullOrEmpty(token))
            return null;
        var client = httpClientFactory.CreateClient("email-provider");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static string DecodeBase64Url(string data) => Encoding.UTF8.GetString(DecodeBase64UrlBytes(data));

    private static byte[] DecodeBase64UrlBytes(string data)
    {
        var s = data.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return []; }
    }

    private static IEnumerable<string> SplitAddresses(string? header) =>
        string.IsNullOrWhiteSpace(header)
            ? []
            : header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ExtractEmail).OfType<string>();

    private static string? ExtractEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var start = value.IndexOf('<');
        var end = value.IndexOf('>');
        return start >= 0 && end > start ? value[(start + 1)..end].Trim() : value.Trim();
    }

    private static string? Str(JsonElement el, string property) =>
        el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static EmailFolderKind MapKind(string labelId) =>
        labelId.ToUpperInvariant() switch
        {
            "INBOX" => EmailFolderKind.Inbox,
            "SENT" => EmailFolderKind.Sent,
            "DRAFT" => EmailFolderKind.Drafts,
            "TRASH" => EmailFolderKind.Trash,
            "SPAM" => EmailFolderKind.Spam,
            _ => EmailFolderKind.Custom,
        };

    private static readonly Error NoToken = new("EmailAccount.ProviderNotConfigured", "No valid Gmail access token (configure EmailOAuth:Gmail and reconnect).");
}
