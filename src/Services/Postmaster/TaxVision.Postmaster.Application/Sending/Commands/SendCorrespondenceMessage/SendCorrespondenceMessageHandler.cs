using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;

/// <summary>
/// Punto de entrada real de "Correspondence redacta, Postmaster envía" (D3 Compose §7/§16 Fase 5).
/// A diferencia de <see cref="Consumers.NotificationsEmailSendRequestedConsumer"/> es una llamada M2M
/// síncrona (la respuesta HTTP lleva el resultado real, no un callback async) — mismo criterio de
/// idempotencia por reserva previa, pero acá la clave es <see cref="SendCorrespondenceMessageCommand.CorrespondenceDraftId"/>:
/// reintentar el mismo draft nunca duplica el envío.
/// </summary>
public static class SendCorrespondenceMessageHandler
{
    public static async Task<Result<SendCorrespondenceMessageResult>> Handle(
        SendCorrespondenceMessageCommand command,
        IOAuthProviderResolver oauthProviderResolver,
        ISuppressionListRepository suppressionList,
        IOutboundAttachmentFetcher attachmentFetcher,
        IOAuthEmailSender oauthEmailSender,
        ISentMessageRepository sentMessages,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var idempotencyKey = BuildIdempotencyKey(command.CorrespondenceDraftId);

        var replay = await ReserveIdempotencyAsync(command, idempotencyKey, idempotencyGuard, ct);
        if (replay is not null)
            return replay;

        var resolveResult = await ResolveAccountAsync(command, oauthProviderResolver, ct);
        if (resolveResult.IsFailure)
            return Result.Failure<SendCorrespondenceMessageResult>(resolveResult.Error);
        var provider = resolveResult.Value;

        var persistResult = await PersistQueuedMessageAsync(
            command,
            provider,
            idempotencyKey,
            sentMessages,
            unitOfWork,
            ct
        );
        if (persistResult.IsFailure)
            return Result.Failure<SendCorrespondenceMessageResult>(persistResult.Error);
        var message = persistResult.Value;

        var suppressed = await ApplySuppressionAsync(
            message,
            idempotencyKey,
            suppressionList,
            idempotencyGuard,
            unitOfWork,
            ct
        );
        if (suppressed is not null)
            return suppressed;

        var sendResult = await FetchAttachmentsAndSendAsync(
            message,
            command,
            provider,
            attachmentFetcher,
            oauthEmailSender,
            idempotencyKey,
            idempotencyGuard,
            unitOfWork,
            ct
        );
        if (sendResult.IsFailure)
            return Result.Failure<SendCorrespondenceMessageResult>(sendResult.Error);

        return await FinalizeAsync(message, sendResult.Value, idempotencyKey, idempotencyGuard, unitOfWork, ct);
    }

    private static string BuildIdempotencyKey(Guid correspondenceDraftId) =>
        $"correspondence-draft:{correspondenceDraftId}";

    /// <summary>
    /// Reserva previa a crear cualquier <see cref="SentMessage"/> — un reintento del mismo draft
    /// devuelve el resultado ya materializado sin reenviar. Tri-state real (plan §Fase 11):
    /// <c>AlreadyCompleted</c> es el replay limpio de siempre; <c>InProgress</c> — otro intento
    /// concurrente con el mismo draft todavía procesando — antes se conflaba con "reserva nueva" y
    /// producía un segundo <c>SentMessage</c> en la carrera. Ahora devuelve un error 409 real (mapeado
    /// en <c>ErrorHttpMapping</c>) en vez de dejar que el caller (Correspondence) reintente ciegamente
    /// o asuma éxito.
    /// </summary>
    private static async Task<Result<SendCorrespondenceMessageResult>?> ReserveIdempotencyAsync(
        SendCorrespondenceMessageCommand command,
        string idempotencyKey,
        IIdempotencyGuard idempotencyGuard,
        CancellationToken ct
    )
    {
        var reservation = await idempotencyGuard.TryReserveAsync(command.TenantId, idempotencyKey, ct);
        return reservation.Outcome switch
        {
            IdempotencyReservationOutcome.AlreadyCompleted => Result.Success(
                new SendCorrespondenceMessageResult(reservation.ExistingSentMessageId!.Value, ProviderMessageId: null)
            ),
            IdempotencyReservationOutcome.InProgress => Result.Failure<SendCorrespondenceMessageResult>(
                InProgressError
            ),
            _ => null,
        };
    }

    /// <summary>Compartido entre el race-loss del guard y el backstop de <see cref="PersistQueuedMessageAsync"/> — ambos son la misma condición de negocio ("ya está en proceso"), no dos errores distintos.</summary>
    private static readonly Error InProgressError = new(
        "SendCorrespondenceMessageHandler.SendInProgress",
        "This message is already being sent by a concurrent request. Check back shortly."
    );

    /// <summary>
    /// A diferencia del canal automático, la cuenta la elige explícitamente el preparador
    /// (<see cref="SendCorrespondenceMessageCommand.AccountId"/>) — <see cref="IOAuthProviderResolver.ResolveByAccountIdAsync"/>
    /// ya valida tenant+activa (D3 Compose §11.4/§15), acá solo se traduce el resultado a error HTTP.
    /// </summary>
    private static async Task<Result<ResolvedOAuthProvider>> ResolveAccountAsync(
        SendCorrespondenceMessageCommand command,
        IOAuthProviderResolver oauthProviderResolver,
        CancellationToken ct
    )
    {
        var resolveResult = await oauthProviderResolver.ResolveByAccountIdAsync(
            command.TenantId,
            command.AccountId,
            ct
        );
        return resolveResult.Status == OAuthResolutionStatus.Resolved
            ? Result.Success(resolveResult.Provider!)
            : Result.Failure<ResolvedOAuthProvider>(
                new Error(
                    "SendCorrespondenceMessageHandler.AccountNotFound",
                    "The selected account is not connected or is not active for this tenant."
                )
            );
    }

    /// <summary>
    /// Defensa-en-profundidad (plan §Fase 11, punto 4): aun con la reserva de idempotencia ya tomada
    /// por este caller, el índice único real de <c>SentMessages</c> (<c>SentMessageConfiguration</c>)
    /// es el backstop final para la ventana angosta donde dos reservas podrían en teoría leer "no
    /// existe" antes de que cualquiera escriba. Antes de este fix, un <c>ConflictException</c> acá
    /// subía sin atrapar hasta <c>ExceptionHandlingMiddleware</c> como un 500 genérico — ahora se
    /// traduce al mismo error 409 que el race-loss del guard (<see cref="InProgressError"/>).
    /// </summary>
    private static async Task<Result<SentMessage>> PersistQueuedMessageAsync(
        SendCorrespondenceMessageCommand command,
        ResolvedOAuthProvider provider,
        string idempotencyKey,
        ISentMessageRepository sentMessages,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var queueResult = SentMessage.Queue(
            command.TenantId,
            idempotencyKey,
            command.Subject,
            provider.FromAddress,
            EmailStream.Transactional,
            provider.ProviderCode,
            notificationLogId: null,
            correlationId: null,
            provider.FromDisplayName,
            replyTo: null,
            templateKey: null,
            DateTime.UtcNow,
            ProviderScope.TenantOAuth,
            command.CorrespondenceDraftId,
            command.InReplyToInternetMessageId,
            command.References
        );
        if (queueResult.IsFailure)
            throw new InvalidOperationException(
                $"Malformed {nameof(SendCorrespondenceMessageCommand)}: {queueResult.Error.Message}"
            );

        var message = queueResult.Value;
        foreach (var to in command.To)
            message.AddRecipient(to, RecipientType.To, null);
        foreach (var cc in command.Cc)
            message.AddRecipient(cc, RecipientType.Cc, null);
        foreach (var bcc in command.Bcc)
            message.AddRecipient(bcc, RecipientType.Bcc, null);
        message.RecordAttachments(command.Attachments);

        await sentMessages.AddAsync(message, ct);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConflictException)
        {
            return Result.Failure<SentMessage>(InProgressError);
        }
        return Result.Success(message);
    }

    /// <summary>Si TODOS los recipients quedan suprimidos, el mensaje entero pasa a Suppressed y el caller devuelve error sin intentar el envío — mismo criterio que el canal automático.</summary>
    private static async Task<Result<SendCorrespondenceMessageResult>?> ApplySuppressionAsync(
        SentMessage message,
        string idempotencyKey,
        ISuppressionListRepository suppressionList,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var addresses = message.Recipients.Select(r => r.Address).Distinct().ToList();
        var suppressed = await suppressionList.GetSuppressedAsync(message.TenantId, addresses, ct);
        if (suppressed.Count == 0)
            return null;

        var now = DateTime.UtcNow;
        foreach (var recipient in message.Recipients.Where(r => suppressed.Contains(r.Address)))
            message.RecordDeliveryEvent(
                recipient.Id,
                SentMessageEventType.Suppressed,
                now,
                rawPayload: null,
                "Address in suppression list."
            );

        if (!message.Recipients.All(r => r.Status == RecipientStatus.Suppressed))
        {
            await unitOfWork.SaveChangesAsync(ct);
            return null;
        }

        message.MarkAsSuppressed("All recipients are in the suppression list.", now);
        await idempotencyGuard.CompleteAsync(message.TenantId, idempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Failure<SendCorrespondenceMessageResult>(
            new Error(
                "SendCorrespondenceMessageHandler.AllRecipientsSuppressed",
                "All recipients are in the suppression list."
            )
        );
    }

    /// <summary>Trae los bytes reales de CloudStorage recién acá — nunca antes de saber que el mensaje va a intentar enviarse (D3 Compose §12).</summary>
    private static async Task<Result<SendResult>> FetchAttachmentsAndSendAsync(
        SentMessage message,
        SendCorrespondenceMessageCommand command,
        ResolvedOAuthProvider provider,
        IOutboundAttachmentFetcher attachmentFetcher,
        IOAuthEmailSender oauthEmailSender,
        string idempotencyKey,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var fetchResult = await attachmentFetcher.FetchAllAsync(command.TenantId, command.Attachments, ct);
        if (fetchResult.IsFailure)
        {
            var now = DateTime.UtcNow;
            message.MarkAsFailed($"AttachmentFetchFailed: {fetchResult.Error.Message}", now);
            await idempotencyGuard.CompleteAsync(command.TenantId, idempotencyKey, message.Id, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<SendResult>(
                new Error("SendCorrespondenceMessageHandler.AttachmentFetchFailed", fetchResult.Error.Message)
            );
        }

        message.MarkAsSending();
        var content = new RenderedContent(command.Subject, command.Html, command.Text);
        var sendResult = await oauthEmailSender.SendAsync(
            message,
            content,
            provider,
            command.InReplyToInternetMessageId,
            command.References,
            command.ReplyToProviderMessageId,
            fetchResult.Value,
            ct
        );
        return Result.Success(sendResult);
    }

    private static async Task<Result<SendCorrespondenceMessageResult>> FinalizeAsync(
        SentMessage message,
        SendResult sendResult,
        string idempotencyKey,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        if (sendResult.Success)
            message.MarkAsSent(sendResult.ProviderMessageId, now);
        else
            message.MarkAsFailed(sendResult.ErrorReason ?? "Unknown Connectors send failure.", now);

        await idempotencyGuard.CompleteAsync(message.TenantId, idempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return sendResult.Success
            ? Result.Success(new SendCorrespondenceMessageResult(message.Id, sendResult.ProviderMessageId))
            : Result.Failure<SendCorrespondenceMessageResult>(
                new Error(
                    "SendCorrespondenceMessageHandler.ConnectorsSendFailed",
                    sendResult.ErrorReason ?? "Unknown Connectors send failure."
                )
            );
    }
}
