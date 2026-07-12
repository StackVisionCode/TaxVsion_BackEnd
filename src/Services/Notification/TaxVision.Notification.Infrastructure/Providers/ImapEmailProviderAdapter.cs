using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Infrastructure.Providers;

/// <summary>
/// Adaptador IMAP real con MailKit. Descifra la contraseña de la cuenta internamente. Sincroniza
/// carpetas y mensajes (envelope, flags, snippet, cuerpo HTML/texto, adjuntos) con cursor UIDVALIDITY:UID.
/// </summary>
public sealed class ImapEmailProviderAdapter(ISecretProtector protector, ILogger<ImapEmailProviderAdapter> logger)
    : IEmailProviderAdapter
{
    public EmailExternalProvider Provider => EmailExternalProvider.Imap;

    public async Task<Result<IReadOnlyList<ProviderFolder>>> ListFoldersAsync(
        EmailAccountConnection account,
        CancellationToken ct = default
    )
    {
        try
        {
            using var client = await ConnectAsync(account, ct);
            var folders = new List<ProviderFolder>
            {
                new(client.Inbox.FullName, client.Inbox.Name, EmailFolderKind.Inbox),
            };

            var personal = client.GetFolder(client.PersonalNamespaces[0]);
            foreach (var folder in await personal.GetSubfoldersAsync(false, ct))
            {
                if (string.Equals(folder.FullName, client.Inbox.FullName, StringComparison.OrdinalIgnoreCase))
                    continue;
                folders.Add(new ProviderFolder(folder.FullName, folder.Name, MapKind(folder.Name)));
            }

            await client.DisconnectAsync(true, ct);
            return Result.Success<IReadOnlyList<ProviderFolder>>(folders);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IMAP list folders failed for account {AccountId}.", account.Id);
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
        try
        {
            using var client = await ConnectAsync(account, ct);
            var imapFolder = string.Equals(folder.ExternalId, "INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : await client.GetFolderAsync(folder.ExternalId, ct);

            await imapFolder.OpenAsync(FolderAccess.ReadOnly, ct);

            var (validity, lastUid) = ParseCursor(cursor);
            IList<UniqueId> uids =
                !full && validity == imapFolder.UidValidity && lastUid > 0
                    ? await imapFolder.SearchAsync(
                        SearchQuery.Uids(new UniqueIdRange(new UniqueId(lastUid + 1), UniqueId.MaxValue)),
                        ct
                    )
                    : await imapFolder.SearchAsync(SearchQuery.All, ct);

            var toFetch = uids.OrderByDescending(u => u.Id).Take(maxMessages).OrderBy(u => u.Id).ToList();
            var messages = new List<ProviderMessage>();
            var maxUid = lastUid;

            if (toFetch.Count > 0)
            {
                var summaries = await imapFolder.FetchAsync(
                    toFetch,
                    MessageSummaryItems.UniqueId
                        | MessageSummaryItems.Envelope
                        | MessageSummaryItems.Flags
                        | MessageSummaryItems.PreviewText
                        | MessageSummaryItems.BodyStructure,
                    ct
                );

                foreach (var summary in summaries)
                {
                    var mime = await imapFolder.GetMessageAsync(summary.UniqueId, ct);
                    messages.Add(MapMessage(summary, mime));
                    if (summary.UniqueId.Id > maxUid)
                        maxUid = summary.UniqueId.Id;
                }
            }

            var count = imapFolder.Count;
            var newCursor = $"{imapFolder.UidValidity}:{maxUid}";
            await client.DisconnectAsync(true, ct);
            return Result.Success(new ProviderFolderSync(messages, newCursor, count));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IMAP sync folder {Folder} failed for account {AccountId}.", folder.Name, account.Id);
            return Result.Failure<ProviderFolderSync>(new Error("EmailAccount.SyncFailed", ex.Message));
        }
    }

    private async Task<ImapClient> ConnectAsync(EmailAccountConnection account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account.ImapHost) || string.IsNullOrWhiteSpace(account.ImapPasswordCipher))
            throw new InvalidOperationException("IMAP account is missing host or credentials.");

        var password =
            protector.Unprotect(account.ImapPasswordCipher)
            ?? throw new InvalidOperationException("Could not decrypt IMAP credentials.");

        var client = new ImapClient();
        var socket = account.ImapUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
        await client.ConnectAsync(account.ImapHost, account.ImapPort ?? 993, socket, ct);
        await client.AuthenticateAsync(account.ImapUsername ?? account.EmailAddress, password, ct);
        return client;
    }

    private const long MaxAttachmentBytes = 26_214_400; // 25 MB: por encima no se materializa el binario.

    private static ProviderMessage MapMessage(IMessageSummary summary, MimeMessage mime)
    {
        var envelope = summary.Envelope;
        var attachments = mime.Attachments.OfType<MimePart>().Select(ReadAttachment).ToList();

        var threadId = mime.References.FirstOrDefault() ?? mime.InReplyTo ?? mime.MessageId;

        return new ProviderMessage(
            summary.UniqueId.ToString(),
            threadId,
            envelope?.Subject,
            Mailboxes(envelope?.From).FirstOrDefault(),
            Mailboxes(envelope?.To),
            Mailboxes(envelope?.Cc),
            Mailboxes(envelope?.Bcc),
            summary.PreviewText,
            mime.HtmlBody,
            mime.TextBody,
            JsonSerializer.Serialize(new { messageId = envelope?.MessageId }),
            summary.Flags?.HasFlag(MessageFlags.Seen) ?? false,
            summary.Flags?.HasFlag(MessageFlags.Flagged) ?? false,
            envelope?.Date?.UtcDateTime,
            mime.Date == default ? null : mime.Date.UtcDateTime,
            attachments
        );
    }

    private static ProviderAttachment ReadAttachment(MimePart part)
    {
        long size = 0;
        byte[]? content = null;
        if (part.Content is not null)
        {
            using var ms = new MemoryStream();
            part.Content.DecodeTo(ms);
            size = ms.Length;
            if (size is > 0 and <= MaxAttachmentBytes)
                content = ms.ToArray();
        }
        return new ProviderAttachment(
            part.FileName ?? "attachment",
            part.ContentType?.MimeType,
            size,
            part.ContentId,
            content
        );
    }

    private static List<string> Mailboxes(InternetAddressList? list) =>
        list?.Mailboxes.Select(m => m.Address).ToList() ?? [];

    private static (uint Validity, uint LastUid) ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return (0, 0);
        var parts = cursor.Split(':');
        return parts.Length == 2 && uint.TryParse(parts[0], out var v) && uint.TryParse(parts[1], out var u)
            ? (v, u)
            : (0, 0);
    }

    private static EmailFolderKind MapKind(string name) =>
        name.ToLowerInvariant() switch
        {
            "inbox" => EmailFolderKind.Inbox,
            "sent" or "sent items" or "sent mail" => EmailFolderKind.Sent,
            "drafts" => EmailFolderKind.Drafts,
            "archive" or "all mail" => EmailFolderKind.Archive,
            "trash" or "deleted" or "deleted items" or "bin" => EmailFolderKind.Trash,
            "spam" or "junk" or "junk email" => EmailFolderKind.Spam,
            _ => EmailFolderKind.Custom,
        };
}
