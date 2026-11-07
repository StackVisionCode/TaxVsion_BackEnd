using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.Sending.Commands.DispatchSentMessage;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.Sending;
using Wolverine;

namespace TaxVision.Postmaster.Application.Sending.Lanes;

/// <summary>
/// Lo común a los dos carriles en el paso transaccional del envío (PMS-10): encolar el mensaje con sus
/// destinatarios, aplicar la supresión y dejar el despacho publicado después de guardar.
/// </summary>
internal sealed record LanePreparation(
    NotificationsEmailSendRequestedIntegrationEvent Event,
    ISuppressionListRepository SuppressionList,
    ISentMessageRepository SentMessages,
    ITenantDirectoryRepository TenantDirectory,
    IIdempotencyGuard IdempotencyGuard,
    IUnitOfWork UnitOfWork,
    IMessageBus Bus,
    ILogger Logger,
    CancellationToken Ct
)
{
    /// <summary>Motivo del aviso a Notification cuando el To no es una dirección válida.</summary>
    public const string RecipientInvalidReason = "recipient.invalid";

    /// <summary>
    /// Devuelve null si el To no sirve: no se crea un mensaje sin destinatarios, se libera la reserva y
    /// Notification se entera. Reintentar no lo arregla.
    /// </summary>
    public async Task<SentMessage?> PersistAsync(Result<SentMessage> queued)
    {
        if (queued.IsFailure)
            throw new MalformedEmailEventException(
                nameof(NotificationsEmailSendRequestedIntegrationEvent),
                queued.Error
            );

        var message = queued.Value;
        if (AddRecipients(message).IsFailure)
        {
            await RejectInvalidRecipientAsync();
            return null;
        }

        await SentMessages.AddAsync(message, Ct);
        await SaveOrThrowInProgressAsync();
        return message;
    }

    /// <summary>
    /// Marca los destinatarios suprimidos. Devuelve true si no quedó ninguno: el mensaje pasó a Suppressed y
    /// el caller no sigue. Si quedan, <c>MimeMessageBuilder</c> excluye a los suprimidos del MIME real.
    /// </summary>
    public async Task<bool> ApplySuppressionAsync(SentMessage message)
    {
        var addresses = message.Recipients.Select(r => r.Address).Distinct().ToList();
        var suppressed = await SuppressionList.GetSuppressedAsync(Event.TenantId, addresses, Ct);
        if (suppressed.Count == 0)
            return false;

        var allSuppressed = message.ApplySuppression(suppressed, DateTime.UtcNow).Value;
        if (allSuppressed)
            await IdempotencyGuard.CompleteAsync(Event.TenantId, Event.IdempotencyKey, message.Id, Ct);
        await UnitOfWork.SaveChangesAsync(Ct);
        return allSuppressed;
    }

    /// <summary>
    /// Cierre del paso transaccional: el mensaje queda en Sending y el despacho se publica después de
    /// guardar; Wolverine confirma la reserva, el mensaje y el sobre del comando juntos.
    /// </summary>
    public async Task QueueDispatchAsync(SentMessage message, ResolvedMailbox? mailbox)
    {
        message.MarkAsSending();
        await UnitOfWork.SaveChangesAsync(Ct);
        await Bus.PublishAsync(
            new DispatchSentMessageCommand(
                message.Id,
                Event.TenantId,
                Event.IdempotencyKey,
                message.DeliveryLane,
                Event,
                mailbox
            )
        );
    }

    /// <summary>Un Cc o Bcc inválido o repetido se descarta: no le quita el correo al destinatario principal.</summary>
    private Result AddRecipients(SentMessage message)
    {
        var to = message.AddRecipient(Event.To, RecipientType.To, null);
        if (to.IsFailure)
            return Result.Failure(to.Error);

        var dropped = (Event.Cc ?? [])
            .Select(cc => message.AddRecipient(cc, RecipientType.Cc, null))
            .Concat((Event.Bcc ?? []).Select(bcc => message.AddRecipient(bcc, RecipientType.Bcc, null)))
            .Count(added => added.IsFailure);
        if (dropped > 0)
            Logger.LogWarning(
                "Dropped {Count} invalid or repeated Cc/Bcc recipient(s) of NotificationLog {NotificationLogId}.",
                dropped,
                Event.NotificationLogId
            );

        return Result.Success();
    }

    private async Task RejectInvalidRecipientAsync()
    {
        Logger.LogWarning(
            "NotificationLog {NotificationLogId} has an invalid To address; the email will not be sent.",
            Event.NotificationLogId
        );
        await IdempotencyGuard.ReleaseAsync(Event.TenantId, Event.IdempotencyKey, Ct);
        await UnitOfWork.SaveChangesAsync(Ct);
        await Bus.PublishAsync(
            new PostmasterEmailDeliveryFailedIntegrationEvent
            {
                TenantId = Event.TenantId,
                CorrelationId = Event.CorrelationId,
                NotificationLogId = Event.NotificationLogId,
                DispatchAttemptId = Event.DispatchAttemptId,
                SentMessageId = Guid.Empty,
                Reason = RecipientInvalidReason,
                CampaignId = Event.CampaignId,
                EventAtUtc = DateTime.UtcNow,
            }
        );
    }

    /// <summary>
    /// Red de seguridad de defensa-en-profundidad (plan §Fase 11, punto 4): aun con el tri-state de
    /// <see cref="IIdempotencyGuard"/> corregido, dos reservas concurrentes podrían en teoría leer
    /// "no existe" antes de que cualquiera de las dos escriba — el índice único real de
    /// <c>SentMessages</c> (<c>SentMessageConfiguration</c>) es el backstop final para esa ventana
    /// angosta. Se trata igual que <see cref="IdempotencyReservationOutcome.InProgress"/>: nunca debe
    /// llegar como una excepción sin manejar hasta el middleware genérico.
    /// </summary>
    private async Task SaveOrThrowInProgressAsync()
    {
        try
        {
            await UnitOfWork.SaveChangesAsync(Ct);
        }
        catch (ConflictException)
        {
            throw new IdempotencyReservationInProgressException(Event.IdempotencyKey);
        }
    }
}
