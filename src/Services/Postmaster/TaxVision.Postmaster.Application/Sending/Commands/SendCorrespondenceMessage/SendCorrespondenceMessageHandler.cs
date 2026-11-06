using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Sending.Commands.DiscardUnsentMessage;
using TaxVision.Postmaster.Application.Sending.Commands.MarkSendAttempted;
using TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;

/// <summary>
/// Punto de entrada real de "Correspondence redacta, Postmaster envía" (D3 Compose §7/§16 Fase 5). Llamada
/// M2M síncrona: la respuesta HTTP lleva el resultado real. Sin transacción propia (PMS-01): prepara en una
/// transacción corta (<see cref="PrepareCorrespondenceMessageHandler"/>), graba el intento, llama a Connectors
/// y registra el resultado en otra. Reintentar el mismo draft nunca duplica el envío.
/// </summary>
public static class SendCorrespondenceMessageHandler
{
    [NonTransactional]
    public static async Task<Result<SendCorrespondenceMessageResult>> Handle(
        SendCorrespondenceMessageCommand command,
        IOutboundAttachmentFetcher attachmentFetcher,
        IConnectedMailboxSender mailboxSender,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var prepared = await bus.InvokeAsync<Result<PreparedCorrespondenceMessage>>(
            new PrepareCorrespondenceMessageCommand(command),
            ct
        );
        if (prepared.IsFailure)
            return Result.Failure<SendCorrespondenceMessageResult>(prepared.Error);
        if (prepared.Value.Completed is { } completed)
            return Result.Success(completed);

        var message = prepared.Value.Message!;

        // Los bytes de CloudStorage recién acá, cuando se sabe que el mensaje va a intentar salir (D3 Compose §12).
        var fetchResult = await attachmentFetcher.FetchAllAsync(command.TenantId, command.Attachments, ct);
        if (fetchResult.IsFailure)
        {
            // Nada salió: se deshace el intento (SentMessage y reserva) para que el mismo draft reintente limpio.
            await bus.InvokeAsync(new DiscardUnsentMessageCommand(message.Id), ct);
            return Result.Failure<SendCorrespondenceMessageResult>(
                new Error("SendCorrespondenceMessageHandler.AttachmentFetchFailed", fetchResult.Error.Message)
            );
        }

        var attempted = await bus.InvokeAsync<Result>(new MarkSendAttemptedCommand(message.Id), ct);
        if (attempted.IsFailure)
            return Result.Success(new SendCorrespondenceMessageResult(message.Id, null, InProgress: true));

        var sendResult = await mailboxSender.SendAsync(
            message,
            new RenderedContent(command.Subject, command.Html, command.Text),
            prepared.Value.Mailbox!,
            command.InReplyToInternetMessageId,
            command.References,
            command.ReplyToProviderMessageId,
            fetchResult.Value,
            // Correspondence manda lo que escribio el usuario: no hay cascara de plantilla ni logo cid.
            inlineAssets: [],
            ct
        );

        // Se registra aunque el pedido HTTP se haya cancelado: Connectors ya contestó.
        await bus.InvokeAsync(
            new RecordSendResultCommand(
                message.Id,
                sendResult,
                "Unknown Connectors send failure.",
                Fallback: null
            ),
            CancellationToken.None
        );

        return sendResult.Success
            ? Result.Success(new SendCorrespondenceMessageResult(
                    message.Id,
                    sendResult.ProviderMessageId,
                    InternetMessageId: sendResult.InternetMessageId
                ))
            : Result.Failure<SendCorrespondenceMessageResult>(
                new Error(
                    "SendCorrespondenceMessageHandler.ConnectorsSendFailed",
                    sendResult.ErrorReason ?? "Unknown Connectors send failure."
                )
            );
    }
}
