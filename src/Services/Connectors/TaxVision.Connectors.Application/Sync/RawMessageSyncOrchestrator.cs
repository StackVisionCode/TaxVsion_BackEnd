using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Lógica compartida entre ProcessGmailPushNotificationHandler y ProcessGraphNotificationHandler:
/// ambos terminan resolviendo (cuenta, client, cursor) por caminos distintos y de ahí en más el
/// flujo es idéntico. Fetch history → por cada mensaje nuevo, metadata-only (nunca body/attachments,
/// regla dura del plan §19) → publish → avanzar cursor. El fetch corre sin transacción; lo publicado y el cursor
/// se confirman juntos en una unidad corta, dentro del lock de la cuenta (CON-03).
/// </summary>
internal static class RawMessageSyncOrchestrator
{
    private const int MaxSnippetLength = 500;

    /// <summary>La cuenta necesita reconectarse: reintentar el sync no lo arregla.</summary>
    public const string ReconnectRequiredCode = "RawMessageSync.ReconnectRequired";

    /// <summary>Algún mensaje falló de forma transitoria: el cursor no avanza y la próxima pasada lo repite.</summary>
    public const string MessageFetchPartiallyFailedCode = "RawMessageSync.MessageFetchPartiallyFailed";

    /// <summary>Devuelve, en éxito, la cantidad de mensajes efectivamente publicados en esta corrida — ReconcileAccountHandler la usa para distinguir "no encontró nada nuevo" de "encontró mensajes que el push no había entregado" (ver ReconciliationOutcome).</summary>
    public static async Task<Result<int>> SyncAndPublishAsync(
        TenantEmailAccount account,
        IEmailProviderClient client,
        ProviderSyncCursor cursor,
        IOutboxUnit unit,
        ICorrelationContext correlation,
        ILogger logger,
        MessageFetchFailureTracker fetchFailures,
        CancellationToken ct
    )
    {
        HistoryPage history;
        try
        {
            history = await client.GetHistoryAsync(
                account.Id,
                account.TenantId,
                cursor.CursorValue,
                account.ConnectedAtUtc,
                ct
            );
        }
        catch (EmailProviderException ex)
        {
            var code = ex.RequiresReconnect ? ReconnectRequiredCode : "RawMessageSync.HistoryFetchFailed";
            return Result.Failure<int>(new Error(code, ex.Message));
        }

        IReadOnlyList<ProviderMessageFetch> fetched = [];
        if (history.NewMessageIds.Count > 0)
        {
            try
            {
                fetched = await client.GetMessagesAsync(account.Id, account.TenantId, history.NewMessageIds, ct);
            }
            catch (EmailProviderException ex)
            {
                // Falló el lote entero: sin avanzar el cursor, la próxima pasada lo reintenta.
                var code = ex.RequiresReconnect ? ReconnectRequiredCode : "RawMessageSync.MessageFetchFailed";
                return Result.Failure<int>(new Error(code, ex.Message));
            }
        }

        var publishedCount = 0;
        var blockingFailures = 0;
        await unit.RunAsync(
            async (outbox, token) =>
            {
                publishedCount = await PublishFetchedAsync(account, fetched, outbox, correlation, logger);
                if (fetched.Count > 0)
                    blockingFailures = await fetchFailures.RecordAsync(account, fetched, DateTime.UtcNow, token);

                var now = DateTime.UtcNow;
                account.TouchActivity(now);

                // Gmail (historyId) y Graph (deltaLink) no avanzan a medias: se conserva el cursor entero.
                if (blockingFailures == 0)
                    cursor.UpdateCursor(history.NextCursor, now);
                return true;
            },
            ct
        );

        return blockingFailures > 0
            ? Result.Failure<int>(
                new Error(
                    MessageFetchPartiallyFailedCode,
                    $"{blockingFailures} message(s) could not be fetched; the cursor was not advanced."
                )
            )
            : Result.Success(publishedCount);
    }

    private static async Task<int> PublishFetchedAsync(
        TenantEmailAccount account,
        IReadOnlyList<ProviderMessageFetch> fetched,
        Wolverine.IMessageBus outbox,
        ICorrelationContext correlation,
        ILogger logger
    )
    {
        var publishedCount = 0;
        foreach (var item in fetched)
        {
            if (item.Message is null)
            {
                if (item.IsNotFound)
                    logger.LogInformation(
                        "Message {MessageId} on account {AccountId} no longer exists at the provider — skipping.",
                        item.ProviderMessageId,
                        account.Id
                    );
                else
                    logger.LogWarning(
                        item.Error,
                        "Could not fetch metadata for message {MessageId} on account {AccountId} — the cursor stays put.",
                        item.ProviderMessageId,
                        account.Id
                    );
                continue;
            }

            await outbox.PublishAsync(BuildEvent(account, item.Message, correlation.CorrelationId));
            publishedCount++;

            // Un aviso de no entrega se revisa aparte: bajar el MIME no debe frenar el sync.
            if (DeliveryReportCandidate.IsLikely(item.Message))
                await outbox.PublishAsync(
                    new InspectDeliveryReport(
                        account.Id,
                        account.TenantId,
                        item.Message.ProviderMessageId,
                        item.Message.ReceivedAtUtc,
                        correlation.CorrelationId
                    )
                );
        }

        return publishedCount;
    }

    internal static ConnectorsRawMessageReceivedIntegrationEvent BuildEvent(
        TenantEmailAccount account,
        RawMessage message,
        string correlationId,
        bool isReplay = false
    ) =>
        new()
        {
            IsReplay = isReplay,
            TenantId = account.TenantId,
            CorrelationId = correlationId,
            AccountId = account.Id,
            MailboxOwnerUserId = account.OwnerUserId,
            MailboxOwnershipKnown = true,
            ProviderCode = account.ProviderCode.ToString(),
            ProviderMessageId = message.ProviderMessageId,
            ProviderThreadId = message.ProviderThreadId,
            InternetMessageId = message.InternetMessageId,
            InReplyTo = message.InReplyTo,
            References = message.References,
            From = message.From,
            FromDisplayName = message.FromDisplayName,
            To = message.To,
            Cc = message.Cc,
            Bcc = message.Bcc,
            Subject = message.Subject,
            Snippet = message.Snippet.Length > MaxSnippetLength ? message.Snippet[..MaxSnippetLength] : message.Snippet,
            ReceivedAtUtc = message.ReceivedAtUtc,
            HasAttachments = message.HasAttachments,
            AttachmentCount = message.Attachments.Count,
            AttachmentMetadata =
                message.Attachments.Count > 0
                    ? message
                        .Attachments.Select(a => new ConnectorsRawMessageAttachmentMetadata(
                            a.Filename,
                            a.ContentType,
                            a.SizeBytes,
                            a.ProviderAttachmentId,
                            a.PartId
                        ))
                        .ToList()
                    : null,
            SpfResult = message.AuthenticationSignals.SpfResult.ToString(),
            DkimResult = message.AuthenticationSignals.DkimResult.ToString(),
            DmarcResult = message.AuthenticationSignals.DmarcResult.ToString(),
        };
}
