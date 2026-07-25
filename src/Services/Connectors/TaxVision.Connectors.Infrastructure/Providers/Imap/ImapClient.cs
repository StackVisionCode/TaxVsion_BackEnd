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
///
/// Esta clase está registrada Scoped (un scope = un ReconcileAccountCommand / notificación de
/// push), así que cachear la conexión autenticada como campo de instancia y reusarla entre
/// llamadas dentro del mismo pase es seguro — evita reabrir TCP+TLS+AUTH por cada mensaje. Vive
/// exactamente lo que dura el pase y se cierra sola cuando el contenedor descarta el scope
/// (<see cref="DisposeAsync"/>).
/// </summary>
public sealed class ImapClient(
    IImapCredentialsRepository credentialsRepository,
    IEncryptedSecretProtector protector,
    IProviderRateLimiter rateLimiter,
    ProviderCircuitBreakerRegistry circuitBreakers,
    ILogger<ImapClient> logger
) : IEmailProviderClient, IAsyncDisposable
{
    private MailKitImapClient? cachedConnection;
    private Guid cachedAccountId;

    public ProviderCode ProviderCode => ProviderCode.Imap;

    /// <summary>
    /// Tope de UIDs devueltos por pase (mismo criterio que MaxHistoryPages/MaxDeltaPages en
    /// Gmail/Graph — allá el propio API pagina, acá no: SearchAsync siempre devuelve el rango
    /// entero de una sola vez). Sin este tope, una cuenta recién conectada con un inbox grande
    /// volcaría todos los UIDs nuevos en una sola HistoryPage, y RawMessageSyncOrchestrator los
    /// procesa uno por uno dentro de la única transacción ambiente de Wolverine para todo el
    /// ReconcileAccountCommand — manteniéndola abierta minutos y bloqueando el durability agent.
    /// </summary>
    private const int MaxMessagesPerSync = 100;

    /// <summary>
    /// Dos carriles para que un backlog histórico grande nunca bloquee la detección de correo
    /// nuevo (decisión de producto: priorizar reciente, backfill de fondo sin bloquear): el live
    /// lane siempre gana el presupuesto del pase — si el gap incremental excede
    /// MaxMessagesPerSync, salta directo al UID más alto real (correo nuevo visible YA en el
    /// próximo pase) y lo que quedó atrás se registra como una ventana de backfill
    /// (BackfillLastUid/BackfillCeiling en ImapCursor) que solo consume el presupuesto que el live
    /// lane no usó ese pase — nunca compite con correo nuevo.
    /// </summary>
    public Task<HistoryPage> GetHistoryAsync(Guid accountId, string? sinceCursor, CancellationToken ct = default) =>
        ExecuteAsync(
            accountId,
            async (inbox, token) =>
            {
                var parsedCursor = ImapCursor.Parse(sinceCursor);
                var sameValidity = parsedCursor is { } cursor && cursor.UidValidity == inbox.UidValidity;

                var liveBaseline = sameValidity ? parsedCursor!.Value.LiveLastUid : 0;
                var backfillLastUid = sameValidity ? parsedCursor!.Value.BackfillLastUid : null;
                var backfillCeiling = sameValidity ? parsedCursor!.Value.BackfillCeiling : null;

                var liveUids = (
                    sameValidity
                        ? await inbox.SearchAsync(
                            SearchQuery.Uids(new UniqueIdRange(new UniqueId(liveBaseline + 1), UniqueId.MaxValue)),
                            token
                        )
                        : await inbox.SearchAsync(SearchQuery.All, token)
                )
                    .OrderBy(u => u.Id)
                    .ToList();

                var page = new List<UniqueId>(MaxMessagesPerSync);
                var budget = MaxMessagesPerSync;
                uint newLiveLastUid;

                if (liveUids.Count <= budget)
                {
                    // Al día (o casi) — publicar todo, el live lane queda totalmente al corriente.
                    page.AddRange(liveUids);
                    budget -= liveUids.Count;
                    newLiveLastUid = liveUids.Count > 0 ? liveUids[^1].Id : liveBaseline;
                }
                else
                {
                    // Gap grande: los MaxMessagesPerSync UIDs más nuevos van primero para que correo
                    // reciente entre YA — el live lane salta directo al UID máximo real, y todo lo
                    // que quedó atrás se abre/extiende como ventana de backfill de menor prioridad.
                    var newest = liveUids.TakeLast(budget).ToList();
                    page.AddRange(newest);
                    budget = 0;
                    newLiveLastUid = liveUids[^1].Id;

                    var gapFloor = liveBaseline;
                    var gapCeiling = newest[0].Id - 1;
                    backfillLastUid ??= gapFloor;
                    backfillCeiling = backfillCeiling is { } existingCeiling
                        ? Math.Max(existingCeiling, gapCeiling)
                        : gapCeiling;

                    logger.LogInformation(
                        "IMAP account {AccountId}: backlog grande ({FoundCount} mensajes pendientes) — priorizando los {Budget} más recientes; backfill de fondo en UID {BackfillLastUid}-{BackfillCeiling}.",
                        accountId,
                        liveUids.Count,
                        MaxMessagesPerSync,
                        backfillLastUid,
                        backfillCeiling
                    );
                }

                if (
                    budget > 0
                    && backfillLastUid is { } bfLast
                    && backfillCeiling is { } bfCeiling
                    && bfLast < bfCeiling
                )
                {
                    var backfillUids = (
                        await inbox.SearchAsync(
                            SearchQuery.Uids(new UniqueIdRange(new UniqueId(bfLast + 1), new UniqueId(bfCeiling))),
                            token
                        )
                    )
                        .OrderBy(u => u.Id)
                        .Take(budget)
                        .ToList();

                    if (backfillUids.Count > 0)
                    {
                        page.AddRange(backfillUids);
                        backfillLastUid = backfillUids[^1].Id;
                    }
                }

                // Ventana de backfill agotada — la cerramos para que el cursor vuelva al formato
                // simple (sin ventana pendiente) apenas se pone al día con el gap que la abrió.
                if (
                    backfillLastUid is { } finalLast
                    && backfillCeiling is { } finalCeiling
                    && finalLast >= finalCeiling
                )
                {
                    backfillLastUid = null;
                    backfillCeiling = null;
                }

                var hasMore =
                    liveUids.Count > MaxMessagesPerSync || (backfillLastUid.HasValue && backfillCeiling.HasValue);
                var nextCursor = new ImapCursor(
                    inbox.UidValidity,
                    newLiveLastUid,
                    backfillLastUid,
                    backfillCeiling
                ).ToString();

                return new HistoryPage(
                    page.OrderBy(u => u.Id).Select(u => u.Id.ToString()).ToList(),
                    nextCursor,
                    hasMore
                );
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
                    var client = await GetOrConnectAsync(accountId, token);
                    var inbox = client.Inbox;
                    if (!inbox.IsOpen)
                        await inbox.OpenAsync(FolderAccess.ReadOnly, token);
                    return await operation(inbox, token);
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
            // La conexión cacheada puede haber quedado en un estado inválido (server la cortó,
            // timeout de socket, etc.) — se descarta para que el próximo intento reconecte desde
            // cero en vez de reintentar sobre un socket ya roto.
            await DisposeCachedConnectionAsync();
            logger.LogWarning(ex, "IMAP operation failed for account {AccountId}.", accountId);
            throw new EmailProviderException("IMAP operation failed.", ex);
        }
    }

    private async Task<MailKitImapClient> GetOrConnectAsync(Guid accountId, CancellationToken ct)
    {
        if (cachedConnection is { IsConnected: true, IsAuthenticated: true } && cachedAccountId == accountId)
            return cachedConnection;

        await DisposeCachedConnectionAsync();

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
            cachedConnection = client;
            cachedAccountId = accountId;
            return client;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            throw new EmailProviderException("IMAP connect/authenticate failed.", ex);
        }
    }

    private async Task DisposeCachedConnectionAsync()
    {
        if (cachedConnection is null)
            return;

        try
        {
            await cachedConnection.DisconnectAsync(true);
        }
        catch
        {
            // El socket puede ya estar cerrado del lado del servidor — el objetivo acá es liberar
            // el recurso local, no reportar un fallo de desconexión.
        }
        finally
        {
            cachedConnection.Dispose();
            cachedConnection = null;
        }
    }

    public async ValueTask DisposeAsync() => await DisposeCachedConnectionAsync();

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
