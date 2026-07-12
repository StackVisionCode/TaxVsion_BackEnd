using System.Net.Http.Headers;
using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Infrastructure.Providers;

/// <summary>
/// Adaptador Microsoft Graph (Outlook/Office 365) real vía HTTP. Usa el access token OAuth de la cuenta
/// (refrescado si hace falta). Sincroniza carpetas y mensajes; cursor por receivedDateTime.
/// </summary>
public sealed class MicrosoftGraphProviderAdapter(
    OAuthTokenService tokens,
    IHttpClientFactory httpClientFactory,
    ILogger<MicrosoftGraphProviderAdapter> logger
) : IEmailProviderAdapter
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private const long MaxAttachmentBytes = 26_214_400;

    public EmailExternalProvider Provider => EmailExternalProvider.MicrosoftGraph;

    public async Task<Result<IReadOnlyList<ProviderFolder>>> ListFoldersAsync(
        EmailAccountConnection account,
        CancellationToken ct = default
    )
    {
        var client = await CreateClientAsync(account, ct);
        if (client is null)
            return Result.Failure<IReadOnlyList<ProviderFolder>>(NoToken);

        try
        {
            using var doc = await GetJsonAsync(client, $"{GraphBase}/me/mailFolders?$top=100", ct);
            var folders = new List<ProviderFolder>();
            if (doc.RootElement.TryGetProperty("value", out var value))
                foreach (var f in value.EnumerateArray())
                {
                    var id = Str(f, "id");
                    var name = Str(f, "displayName") ?? id ?? "folder";
                    if (id is not null)
                        folders.Add(new ProviderFolder(id, name, MapKind(name)));
                }
            return Result.Success<IReadOnlyList<ProviderFolder>>(folders);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Graph list folders failed for account {AccountId}.", account.Id);
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
            var url =
                $"{GraphBase}/me/mailFolders/{Uri.EscapeDataString(folder.ExternalId)}/messages"
                + $"?$top={maxMessages}&$orderby=receivedDateTime desc";
            if (!full && !string.IsNullOrWhiteSpace(cursor))
                url += $"&$filter={Uri.EscapeDataString($"receivedDateTime gt {cursor}")}";

            using var doc = await GetJsonAsync(client, url, ct);
            var messages = new List<ProviderMessage>();
            string? newCursor = cursor;

            if (doc.RootElement.TryGetProperty("value", out var value))
                foreach (var m in value.EnumerateArray())
                {
                    var received = DateTimeOrNull(m, "receivedDateTime");
                    var pm = await MapMessageAsync(client, account, m, received, ct);
                    messages.Add(pm);
                    if (received is { } r && (newCursor is null || string.CompareOrdinal(FormatIso(r), newCursor) > 0))
                        newCursor = FormatIso(r);
                }

            return Result.Success(new ProviderFolderSync(messages, newCursor, messages.Count));
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Graph sync folder {Folder} failed for account {AccountId}.",
                folder.Name,
                account.Id
            );
            return Result.Failure<ProviderFolderSync>(new Error("EmailAccount.SyncFailed", ex.Message));
        }
    }

    private async Task<ProviderMessage> MapMessageAsync(
        HttpClient client,
        EmailAccountConnection account,
        JsonElement m,
        DateTime? received,
        CancellationToken ct
    )
    {
        var id = Str(m, "id") ?? string.Empty;
        var contentType = m.TryGetProperty("body", out var body) ? Str(body, "contentType") : null;
        var content = m.TryGetProperty("body", out var b2) ? Str(b2, "content") : null;
        var isHtml = string.Equals(contentType, "html", StringComparison.OrdinalIgnoreCase);
        var hasAttachments = m.TryGetProperty("hasAttachments", out var ha) && ha.ValueKind == JsonValueKind.True;

        var attachments = hasAttachments ? await FetchAttachmentsAsync(client, id, ct) : [];

        return new ProviderMessage(
            id,
            Str(m, "conversationId"),
            Str(m, "subject"),
            m.TryGetProperty("from", out var from) && from.TryGetProperty("emailAddress", out var fa)
                ? Str(fa, "address")
                : null,
            ReadRecipients(m, "toRecipients"),
            ReadRecipients(m, "ccRecipients"),
            ReadRecipients(m, "bccRecipients"),
            Str(m, "bodyPreview"),
            isHtml ? content : null,
            isHtml ? null : content,
            null,
            m.TryGetProperty("isRead", out var ir) && ir.ValueKind == JsonValueKind.True,
            m.TryGetProperty("flag", out var fl)
                && fl.TryGetProperty("flagStatus", out var fs)
                && string.Equals(fs.GetString(), "flagged", StringComparison.OrdinalIgnoreCase),
            received,
            DateTimeOrNull(m, "sentDateTime"),
            attachments
        );
    }

    private async Task<IReadOnlyList<ProviderAttachment>> FetchAttachmentsAsync(
        HttpClient client,
        string messageId,
        CancellationToken ct
    )
    {
        try
        {
            using var doc = await GetJsonAsync(client, $"{GraphBase}/me/messages/{messageId}/attachments", ct);
            var result = new List<ProviderAttachment>();
            if (doc.RootElement.TryGetProperty("value", out var value))
                foreach (var a in value.EnumerateArray())
                {
                    var name = Str(a, "name") ?? "attachment";
                    var contentType = Str(a, "contentType");
                    var size = a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s) ? s : 0;
                    byte[]? bytes = null;
                    if (
                        size <= MaxAttachmentBytes
                        && a.TryGetProperty("contentBytes", out var cb)
                        && cb.ValueKind == JsonValueKind.String
                    )
                    {
                        try
                        {
                            bytes = Convert.FromBase64String(cb.GetString()!);
                        }
                        catch (FormatException)
                        {
                            bytes = null;
                        }
                    }
                    result.Add(new ProviderAttachment(name, contentType, size, Str(a, "id"), bytes));
                }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Graph fetch attachments failed for message {MessageId}.", messageId);
            return [];
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

    private static List<string> ReadRecipients(JsonElement message, string property)
    {
        var result = new List<string>();
        if (message.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var r in arr.EnumerateArray())
                if (r.TryGetProperty("emailAddress", out var ea) && Str(ea, "address") is { } addr)
                    result.Add(addr);
        return result;
    }

    private static string? Str(JsonElement el, string property) =>
        el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? DateTimeOrNull(JsonElement el, string property) =>
        el.TryGetProperty(property, out var v)
        && v.ValueKind == JsonValueKind.String
        && DateTime.TryParse(v.GetString(), out var dt)
            ? dt.ToUniversalTime()
            : null;

    private static string FormatIso(DateTime dt) => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static EmailFolderKind MapKind(string name) =>
        name.ToLowerInvariant() switch
        {
            "inbox" => EmailFolderKind.Inbox,
            "sent items" or "sentitems" => EmailFolderKind.Sent,
            "drafts" => EmailFolderKind.Drafts,
            "archive" => EmailFolderKind.Archive,
            "deleted items" or "deleteditems" => EmailFolderKind.Trash,
            "junk email" or "junkemail" => EmailFolderKind.Spam,
            _ => EmailFolderKind.Custom,
        };

    private static readonly Error NoToken = new(
        "EmailAccount.ProviderNotConfigured",
        "No valid Microsoft Graph access token (configure EmailOAuth:Microsoft and reconnect)."
    );
}
