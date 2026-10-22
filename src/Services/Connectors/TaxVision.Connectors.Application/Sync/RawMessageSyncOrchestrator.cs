using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Sync;
using Wolverine;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Lógica compartida entre ProcessGmailPushNotificationHandler y ProcessGraphNotificationHandler:
/// ambos terminan resolviendo (cuenta, client, cursor) por caminos distintos y de ahí en más el
/// flujo es idéntico. Fetch history → por cada mensaje nuevo, metadata-only (nunca body/attachments,
/// regla dura del plan §19) → publish → avanzar cursor.
/// </summary>
internal static class RawMessageSyncOrchestrator
{
    private const int MaxSnippetLength = 500;

    /// <summary>La cuenta necesita reconectarse: reintentar el sync no lo arregla.</summary>
    public const string ReconnectRequiredCode = "RawMessageSync.ReconnectRequired";

    /// <summary>Devuelve, en éxito, la cantidad de mensajes efectivamente publicados en esta corrida — ReconcileAccountHandler la usa para distinguir "no encontró nada nuevo" de "encontró mensajes que el push no había entregado" (ver ReconciliationOutcome).</summary>
    public static async Task<Result<int>> SyncAndPublishAsync(
        TenantEmailAccount account,
        IEmailProviderClient client,
        ProviderSyncCursor cursor,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        HistoryPage history;
        try
        {
            history = await client.GetHistoryAsync(account.Id, account.TenantId, cursor.CursorValue, ct);
        }
        catch (EmailProviderException ex)
        {
            var code = ex.RequiresReconnect ? ReconnectRequiredCode : "RawMessageSync.HistoryFetchFailed";
            return Result.Failure<int>(new Error(code, ex.Message));
        }

        var publishedCount = 0;
        if (history.NewMessageIds.Count > 0)
        {
            IReadOnlyList<ProviderMessageFetch> fetched;
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

            foreach (var item in fetched)
            {
                if (item.Message is null)
                {
                    logger.LogWarning(
                        item.Error,
                        "Could not fetch metadata for message {MessageId} on account {AccountId} — skipping.",
                        item.ProviderMessageId,
                        account.Id
                    );
                    continue;
                }

                await bus.PublishAsync(BuildEvent(account, item.Message, correlation.CorrelationId));
                publishedCount++;

                // Un aviso de no entrega se revisa aparte: bajar el MIME no debe frenar el sync.
                if (DeliveryReportCandidate.IsLikely(item.Message))
                    await bus.PublishAsync(
                        new InspectDeliveryReport(
                            account.Id,
                            account.TenantId,
                            item.Message.ProviderMessageId,
                            item.Message.ReceivedAtUtc,
                            correlation.CorrelationId
                        )
                    );
            }
        }

        var now = DateTime.UtcNow;
        cursor.UpdateCursor(history.NextCursor, now);
        account.TouchActivity(now);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(publishedCount);
    }

    private static ConnectorsRawMessageReceivedIntegrationEvent BuildEvent(
        TenantEmailAccount account,
        RawMessage message,
        string correlationId
    ) =>
        new()
        {
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
