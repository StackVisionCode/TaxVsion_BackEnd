using MailKit;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.RateLimit;
using MailKitImapClient = MailKit.Net.Imap.ImapClient;

namespace TaxVision.Connectors.Infrastructure.Providers.Imap;

/// <summary>
/// Client IMAP puro (MailKit) — la contraparte de lectura de un servidor de correo propio del
/// tenant (SMTP manual en Postmaster solo envía; sin esto esas oficinas no tendrían forma de
/// recibir nada en Correspondence). Inbox-only siempre (D1, §34.5): abre <c>client.Inbox</c>
/// explícitamente, nunca itera otras carpetas. Connect+operate pasa por un
/// <see cref="ProviderCircuitBreaker"/> propio (Fase 10, clave <c>"Imap:messages"</c>) que abre tras
/// fallos consecutivos — a diferencia de Gmail/Graph, acá NO hay retry Polly automático: MailKit no
/// distingue de forma limpia un fallo de red transitorio de un fallo de auth/protocolo a través de su
/// superficie de excepciones, así que forzar reintentos sería una apuesta a ciegas.
/// </summary>
public sealed class ImapClient(
    IImapCredentialsRepository credentialsRepository,
    IEncryptedSecretProtector protector,
    IProviderRateLimiter rateLimiter,
    ProviderCircuitBreakerRegistry circuitBreakers,
    ILogger<ImapClient> logger
) : IEmailProviderClient
{
    public ProviderCode ProviderCode => ProviderCode.Imap;

    public Task<HistoryPage> GetHistoryAsync(Guid accountId, string? sinceCursor, CancellationToken ct = default) =>
        ExecuteAsync(
            accountId,
            async (inbox, token) =>
            {
                var parsedCursor = ImapCursor.Parse(sinceCursor);
                var sameValidity = parsedCursor is { } cursor && cursor.UidValidity == inbox.UidValidity;

                var uids = sameValidity
                    ? await inbox.SearchAsync(
                        SearchQuery.Uids(
                            new UniqueIdRange(new UniqueId(parsedCursor!.Value.LastUid + 1), UniqueId.MaxValue)
                        ),
                        token
                    )
                    : await inbox.SearchAsync(SearchQuery.All, token);

                var newLastUid = uids.Count > 0 ? uids.Max(u => u.Id) : parsedCursor?.LastUid ?? 0;
                var nextCursor = new ImapCursor(inbox.UidValidity, newLastUid).ToString();

                return new HistoryPage(uids.Select(u => u.Id.ToString()).ToList(), nextCursor, HasMore: false);
            },
            ct
        );

    public Task<RawMessage> GetMessageAsync(Guid accountId, string providerMessageId, CancellationToken ct = default) =>
        ExecuteAsync(
            accountId,
            async (inbox, token) =>
            {
                var uid = ParseUid(providerMessageId);
                var summary = (
                    await inbox.FetchAsync(
                        [uid],
                        MessageSummaryItems.UniqueId
                            | MessageSummaryItems.Envelope
                            | MessageSummaryItems.References
                            | MessageSummaryItems.BodyStructure
                            | MessageSummaryItems.Headers,
                        token
                    )
                ).FirstOrDefault();

                if (summary is null)
                    throw new EmailProviderException($"IMAP message with UID {providerMessageId} was not found.");

                var authHeader =
                    summary.Headers?.Contains("Authentication-Results") == true
                        ? summary.Headers["Authentication-Results"]
                        : null;

                return new RawMessage(
                    providerMessageId,
                    null, // IMAP no tiene threadId nativo — Correspondence hilvana por In-Reply-To/References.
                    summary.Envelope?.MessageId,
                    summary.Envelope?.InReplyTo,
                    (summary.References ?? []).ToList(),
                    ExtractMailbox(summary.Envelope?.From),
                    ExtractAddresses(summary.Envelope?.To),
                    ExtractAddresses(summary.Envelope?.Cc),
                    ExtractAddresses(summary.Envelope?.Bcc),
                    summary.Envelope?.Subject ?? string.Empty,
                    string.Empty, // IMAP no tiene snippet — no vale la pena descargar el body para generarlo acá.
                    summary.Envelope?.Date?.UtcDateTime ?? DateTime.UtcNow,
                    ExtractAttachments(summary),
                    AuthenticationResultsHeaderParser.Parse(authHeader)
                );
            },
            ct
        );

    /// <summary>MailKit ya resuelve la "mejor" parte html/text vía BODYSTRUCTURE (summary.HtmlBody/TextBody) — no hace falta caminar el árbol a mano como con Gmail. Octets viene de la propia BODYSTRUCTURE, sin descargas extra.</summary>
    public Task<MessageBody> GetMessageBodyAsync(
        Guid accountId,
        string providerMessageId,
        CancellationToken ct = default
    ) =>
        ExecuteAsync(
            accountId,
            async (inbox, token) =>
            {
                var uid = ParseUid(providerMessageId);
                var summary = (
                    await inbox.FetchAsync(
                        [uid],
                        MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure | MessageSummaryItems.Headers,
                        token
                    )
                ).FirstOrDefault();

                if (summary is null)
                    throw new EmailProviderException($"IMAP message with UID {providerMessageId} was not found.");

                var html = summary.HtmlBody is { } htmlPart
                    ? await FetchTextPartAsync(inbox, uid, htmlPart, token)
                    : null;
                var text = summary.TextBody is { } textPart
                    ? await FetchTextPartAsync(inbox, uid, textPart, token)
                    : null;

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in summary.Headers ?? [])
                    headers.TryAdd(header.Field, header.Value);

                var mimeSize = (summary.HtmlBody?.Octets ?? 0) + (summary.TextBody?.Octets ?? 0);

                return new MessageBody(mimeSize, html, text, headers, ExtractAttachments(summary));
            },
            ct
        );

    private static async Task<string?> FetchTextPartAsync(
        IMailFolder inbox,
        UniqueId uid,
        BodyPartText part,
        CancellationToken ct
    )
    {
        var entity = await inbox.GetBodyPartAsync(uid, part, ct);
        return entity is MimeKit.TextPart textPart ? textPart.Text : null;
    }

    public Task<Stream> GetAttachmentAsync(
        Guid accountId,
        string providerMessageId,
        string attachmentId,
        CancellationToken ct = default
    ) =>
        ExecuteAsync(
            accountId,
            async (inbox, token) =>
            {
                var uid = ParseUid(providerMessageId);
                var summary = (
                    await inbox.FetchAsync(
                        [uid],
                        MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure,
                        token
                    )
                ).FirstOrDefault();

                var part = summary?.Attachments?.FirstOrDefault(a => a.PartSpecifier == attachmentId);
                if (part is null)
                    throw new EmailProviderException(
                        $"IMAP attachment {attachmentId} was not found on message {providerMessageId}."
                    );

                var entity = await inbox.GetBodyPartAsync(uid, part, token);
                var stream = new MemoryStream();
                if (entity is MimeKit.MimePart { Content: not null } mimePart)
                    await mimePart.Content.DecodeToAsync(stream, token);
                stream.Position = 0;
                return (Stream)stream;
            },
            ct
        );

    private async Task<T> ExecuteAsync<T>(
        Guid accountId,
        Func<IMailFolder, CancellationToken, Task<T>> operation,
        CancellationToken ct
    )
    {
        await rateLimiter.WaitForSlotAsync(ProviderCode, ct);

        var breaker = circuitBreakers.GetOrCreate("Imap:messages");
        try
        {
            return await breaker.ExecuteAsync(
                async token =>
                {
                    using var client = await ConnectAsync(accountId, token);
                    var inbox = client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadOnly, token);
                    var result = await operation(inbox, token);
                    await client.DisconnectAsync(true, token);
                    return result;
                },
                ct
            );
        }
        catch (EmailProviderException)
        {
            throw;
        }
        catch (BrokenCircuitException ex)
        {
            throw new EmailProviderException("IMAP circuit breaker is open — too many recent failures.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "IMAP operation failed for account {AccountId}.", accountId);
            throw new EmailProviderException("IMAP operation failed.", ex);
        }
    }

    private async Task<MailKitImapClient> ConnectAsync(Guid accountId, CancellationToken ct)
    {
        var credentialsResult = await credentialsRepository.GetByAccountIdAsync(accountId, ct);
        if (credentialsResult.IsFailure)
            throw new EmailProviderException($"Could not load IMAP credentials: {credentialsResult.Error.Message}");

        var credentials = credentialsResult.Value;
        var password = credentials.PasswordCipher.Decrypt(protector);

        var client = new MailKitImapClient();
        try
        {
            var socketOptions = credentials.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(credentials.Host, credentials.Port, socketOptions, ct);
            await client.AuthenticateAsync(credentials.Username, password, ct);
            return client;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            throw new EmailProviderException("IMAP connect/authenticate failed.", ex);
        }
    }

    private static UniqueId ParseUid(string providerMessageId) =>
        uint.TryParse(providerMessageId, out var value)
            ? new UniqueId(value)
            : throw new EmailProviderException($"'{providerMessageId}' is not a valid IMAP UID.");

    private static string ExtractMailbox(MimeKit.InternetAddressList? addresses) =>
        addresses?.Mailboxes.FirstOrDefault()?.Address ?? string.Empty;

    private static IReadOnlyList<string> ExtractAddresses(MimeKit.InternetAddressList? addresses) =>
        (addresses?.Mailboxes ?? []).Select(m => m.Address).ToList();

    private static IReadOnlyList<RawMessageAttachment> ExtractAttachments(IMessageSummary summary) =>
        (summary.Attachments ?? [])
            .Select(a => new RawMessageAttachment(
                a.PartSpecifier,
                a.FileName ?? "attachment",
                a.ContentType?.MimeType ?? "application/octet-stream",
                a.Octets
            ))
            .ToList();
}
