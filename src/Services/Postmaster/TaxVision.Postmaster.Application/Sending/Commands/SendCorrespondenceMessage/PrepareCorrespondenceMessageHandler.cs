using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;

/// <summary>
/// Paso transaccional de "Correspondence redacta, Postmaster envía" (PMS-01): la reserva por draft y el
/// <see cref="SentMessage"/> en Sending se confirman antes de que el correo salga. El envío lo hace
/// <see cref="SendCorrespondenceMessageHandler"/>, fuera de la transacción.
/// </summary>
public static class PrepareCorrespondenceMessageHandler
{
    /// <summary>Compartido entre el race-loss del guard y el backstop del índice único — la misma condición de negocio ("ya está en proceso").</summary>
    internal static readonly Error InProgressError = new(
        "SendCorrespondenceMessageHandler.SendInProgress",
        "This message is already being sent by a concurrent request. Check back shortly."
    );

    private const string InvalidRecipientCode = "SendCorrespondenceMessageHandler.InvalidRecipient";

    public static async Task<Result<PreparedCorrespondenceMessage>> Handle(
        PrepareCorrespondenceMessageCommand prepare,
        IConnectedMailboxResolver mailboxResolver,
        ISuppressionListRepository suppressionList,
        ISentMessageRepository sentMessages,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        var command = prepare.Request;

        // Antes de reservar: Wolverine guarda lo rastreado aunque se devuelva Failure (COR-19).
        var validTo = ValidateTo(command);
        if (validTo.IsFailure)
            return Result.Failure<PreparedCorrespondenceMessage>(validTo.Error);

        var idempotencyKey = BuildIdempotencyKey(command.CorrespondenceDraftId);

        var replay = await ReserveIdempotencyAsync(command, idempotencyKey, idempotencyGuard, sentMessages, ct);
        if (replay is not null)
            return replay;

        var resolveResult = await ResolveAccountAsync(command, mailboxResolver, ct);
        if (resolveResult.IsFailure)
            return Result.Failure<PreparedCorrespondenceMessage>(resolveResult.Error);
        var provider = resolveResult.Value;

        var persistResult = await PersistQueuedMessageAsync(
            command,
            provider,
            idempotencyKey,
            sentMessages,
            unitOfWork,
            logger,
            ct
        );
        if (persistResult.IsFailure)
            return Result.Failure<PreparedCorrespondenceMessage>(persistResult.Error);
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

        message.MarkAsSending();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new PreparedCorrespondenceMessage(message, provider, Completed: null));
    }

    private static string BuildIdempotencyKey(Guid correspondenceDraftId) =>
        $"correspondence-draft:{correspondenceDraftId}";

    /// <summary>Sin To válidos no hay correo; reintentar no lo arregla. Los Cc y Bcc no lo frenan.</summary>
    private static Result ValidateTo(SendCorrespondenceMessageCommand command)
    {
        if (command.To.Count == 0)
            return Result.Failure(new Error(InvalidRecipientCode, "At least one To recipient is required."));

        var invalid = command.To.Select(EmailAddress.Create).FirstOrDefault(parsed => parsed.IsFailure);
        return invalid is null
            ? Result.Success()
            : Result.Failure(new Error(InvalidRecipientCode, invalid.Error.Message));
    }

    /// <summary>
    /// Reserva previa a crear cualquier <see cref="SentMessage"/> — un reintento del mismo draft devuelve
    /// el resultado ya materializado sin reenviar. <c>InProgress</c> con un SentMessage ya creado es un
    /// envío en curso (o sin resultado registrado): se responde "en curso" con su id, nunca se reenvía.
    /// Sin SentMessage es la carrera de dos pedidos simultáneos: 409.
    /// </summary>
    private static async Task<Result<PreparedCorrespondenceMessage>?> ReserveIdempotencyAsync(
        SendCorrespondenceMessageCommand command,
        string idempotencyKey,
        IIdempotencyGuard idempotencyGuard,
        ISentMessageRepository sentMessages,
        CancellationToken ct
    )
    {
        var reservation = await idempotencyGuard.TryReserveAsync(command.TenantId, idempotencyKey, ct);
        switch (reservation.Outcome)
        {
            case IdempotencyReservationOutcome.AlreadyCompleted:
                // PMS-13: con el Message-ID, el borrador que cierra el job enlaza la respuesta del cliente (COR-16).
                var sent = await sentMessages.FindByIdempotencyKeyAsync(command.TenantId, idempotencyKey, ct);
                return Completed(
                    new SendCorrespondenceMessageResult(
                        reservation.ExistingSentMessageId!.Value,
                        null,
                        InternetMessageId: sent?.InternetMessageId
                    )
                );
            case IdempotencyReservationOutcome.InProgress:
                var existing = await sentMessages.FindByIdempotencyKeyAsync(command.TenantId, idempotencyKey, ct);
                return existing is null
                    ? Result.Failure<PreparedCorrespondenceMessage>(InProgressError)
                    : Completed(new SendCorrespondenceMessageResult(existing.Id, null, InProgress: true));
            default:
                return null;
        }
    }

    private static Result<PreparedCorrespondenceMessage> Completed(SendCorrespondenceMessageResult result) =>
        Result.Success(new PreparedCorrespondenceMessage(null, null, result));

    /// <summary>
    /// A diferencia del canal automático, la cuenta la elige explícitamente el preparador
    /// (<see cref="SendCorrespondenceMessageCommand.AccountId"/>) — <see cref="IConnectedMailboxResolver.ResolveByAccountIdAsync"/>
    /// ya valida tenant+activa (D3 Compose §11.4/§15), acá solo se traduce el resultado a error HTTP.
    /// </summary>
    private static async Task<Result<ResolvedMailbox>> ResolveAccountAsync(
        SendCorrespondenceMessageCommand command,
        IConnectedMailboxResolver mailboxResolver,
        CancellationToken ct
    )
    {
        var resolveResult = await mailboxResolver.ResolveByAccountIdAsync(command.TenantId, command.AccountId, ct);
        return resolveResult.Status == MailboxResolutionStatus.Resolved
            ? Result.Success(resolveResult.Provider!)
            : Result.Failure<ResolvedMailbox>(
                new Error(
                    "SendCorrespondenceMessageHandler.AccountNotFound",
                    "The selected account is not connected or is not active for this tenant."
                )
            );
    }

    /// <summary>
    /// Defensa-en-profundidad (plan §Fase 11, punto 4): el índice único real de <c>SentMessages</c> es el
    /// backstop final para la ventana angosta donde dos reservas podrían leer "no existe" antes de que
    /// cualquiera escriba — se traduce al mismo error 409 que el race-loss del guard.
    /// </summary>
    private static async Task<Result<SentMessage>> PersistQueuedMessageAsync(
        SendCorrespondenceMessageCommand command,
        ResolvedMailbox provider,
        string idempotencyKey,
        ISentMessageRepository sentMessages,
        IUnitOfWork unitOfWork,
        ILogger logger,
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
            ProviderScope.TenantMailbox,
            command.CorrespondenceDraftId,
            command.InReplyToInternetMessageId,
            command.References,
            deliveryLane: DeliveryLane.TenantMailbox
        );
        if (queueResult.IsFailure)
            throw new MalformedEmailEventException(nameof(SendCorrespondenceMessageCommand), queueResult.Error);

        var message = queueResult.Value;
        AddRecipients(message, command, logger);
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

    /// <summary>Un Cc o Bcc inválido o repetido se descarta, como en PMS-10. Del To ya validado solo puede sobrar un repetido.</summary>
    private static void AddRecipients(SentMessage message, SendCorrespondenceMessageCommand command, ILogger logger)
    {
        var dropped = command
            .To.Select(to => message.AddRecipient(to, RecipientType.To, null))
            .Concat(command.Cc.Select(cc => message.AddRecipient(cc, RecipientType.Cc, null)))
            .Concat(command.Bcc.Select(bcc => message.AddRecipient(bcc, RecipientType.Bcc, null)))
            .Count(added => added.IsFailure);
        if (dropped > 0)
            logger.LogWarning(
                "Dropped {Count} invalid or repeated recipient(s) of correspondence draft {DraftId}.",
                dropped,
                command.CorrespondenceDraftId
            );
    }

    /// <summary>Si TODOS los recipients quedan suprimidos, el mensaje entero pasa a Suppressed y se devuelve error sin intentar el envío — mismo criterio que el canal automático.</summary>
    private static async Task<Result<PreparedCorrespondenceMessage>?> ApplySuppressionAsync(
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

        if (!message.ApplySuppression(suppressed, DateTime.UtcNow).Value)
        {
            await unitOfWork.SaveChangesAsync(ct);
            return null;
        }

        await idempotencyGuard.CompleteAsync(message.TenantId, idempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Failure<PreparedCorrespondenceMessage>(
            new Error(
                "SendCorrespondenceMessageHandler.AllRecipientsSuppressed",
                "All recipients are in the suppression list."
            )
        );
    }
}
