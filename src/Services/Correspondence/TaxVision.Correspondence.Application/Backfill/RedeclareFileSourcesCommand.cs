using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Application.Files;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Correspondence.Application.Backfill;

/// <summary>
/// Backfill de File Library: re-declara a CloudStorage los adjuntos de cada enviado de la oficina
/// (<c>email_message</c>/<c>outgoing_attachment</c>) y los entrantes ya guardados
/// (<c>incoming_email</c>/<c>incoming_attachment</c>). En seco por defecto: solo cuenta. Idempotente. Los borradores
/// no.
/// </summary>
public sealed record RedeclareFileSourcesCommand(Guid TenantId, bool DryRun = true);

/// <param name="ResourcesScanned">Enviados y correos recibidos con algún adjunto que declarar.</param>
public sealed record RedeclareFileSourcesReport(bool DryRun, int ResourcesScanned, int FilesDeclared);

/// <summary>Páginas de correos; cada página publica en su propia transacción con outbox. No modifica correos.</summary>
public static class RedeclareFileSourcesHandler
{
    public const int PageSize = 500;

    [NonTransactional]
    public static async Task<RedeclareFileSourcesReport> Handle(
        RedeclareFileSourcesCommand command,
        IDraftRepository drafts,
        IIncomingEmailRepository incomingEmails,
        IOutboxUnit outboxUnit,
        ILogger<RedeclareFileSourcesCommand> logger,
        CancellationToken ct
    )
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var sent = await RedeclareAsync(
            command,
            outboxUnit,
            skip => drafts.ListSentWithAttachmentsAsync(command.TenantId, skip, PageSize, ct),
            draft => draft.Attachments.Count,
            (bus, draft) => SendDraftHandler.PublishAttachmentSourcesAsync(bus, draft, correlationId),
            ct
        );
        var incoming = await RedeclareAsync(
            command,
            outboxUnit,
            skip => incomingEmails.ListWithStoredAttachmentsAsync(command.TenantId, skip, PageSize, ct),
            email => email.Attachments.Count(a => a.CloudStorageFileId is not null),
            (bus, email) => IncomingFileSource.PublishAsync(bus, email, correlationId),
            ct
        );

        logger.LogInformation(
            "File source redeclaration for tenant {TenantId} (dry run: {DryRun}): {Sent} sent and {Incoming} received emails, {Declared} files.",
            command.TenantId,
            command.DryRun,
            sent.Scanned,
            incoming.Scanned,
            sent.Declared + incoming.Declared
        );
        return new RedeclareFileSourcesReport(
            command.DryRun,
            sent.Scanned + incoming.Scanned,
            sent.Declared + incoming.Declared
        );
    }

    // En seco solo cuenta; en real, un outbox por página.
    private static async Task<(int Scanned, int Declared)> RedeclareAsync<T>(
        RedeclareFileSourcesCommand command,
        IOutboxUnit outboxUnit,
        Func<int, Task<IReadOnlyList<T>>> pageAt,
        Func<T, int> filesOf,
        Func<IMessageBus, T, Task> publish,
        CancellationToken ct
    )
    {
        var scanned = 0;
        var declared = 0;
        for (var skip = 0; ; skip += PageSize)
        {
            var page = await pageAt(skip);
            scanned += page.Count;
            declared += page.Sum(filesOf);
            if (!command.DryRun && page.Count > 0)
                await outboxUnit.RunAsync(
                    async (bus, _) =>
                    {
                        foreach (var item in page)
                            await publish(bus, item);
                        return true;
                    },
                    ct
                );
            if (page.Count < PageSize)
                break;
        }
        return (scanned, declared);
    }
}
