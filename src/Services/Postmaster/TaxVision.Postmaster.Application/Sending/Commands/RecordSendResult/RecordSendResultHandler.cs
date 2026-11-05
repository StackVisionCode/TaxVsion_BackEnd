using BuildingBlocks.Persistence;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Consumers;
using TaxVision.Postmaster.Domain.Sending;
using Wolverine;

namespace TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;

/// <summary>Estado del mensaje, cierre de la reserva y aviso a Notification en la misma transacción (outbox).</summary>
public static class RecordSendResultHandler
{
    public static async Task Handle(
        RecordSendResultCommand command,
        ISentMessageRepository sentMessages,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var found = await sentMessages.GetByIdForFeedbackAsync(command.SentMessageId, ct);
        if (found.IsFailure || found.Value.Status != SentMessageStatus.Sending)
            return; // ya registrado

        var message = found.Value;
        // La vía del sistema se aplica acá si el paso de envío no la dejó ya en este mismo objeto.
        if (command.Fallback is { } fallback && message.DeliveryLane == DeliveryLane.TenantMailbox)
            fallback.ApplyTo(message);

        var now = DateTime.UtcNow;
        var result = command.Result;
        SendResultRecorder.Record(message, result, command.FallbackFailureReason, now);
        await idempotencyGuard.CompleteAsync(message.TenantId, message.IdempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);

        if (command.Request is not { } evt)
            return;
        if (result.Success)
            await NotificationsEmailSendRequestedConsumer.PublishSucceededAsync(
                bus,
                evt,
                message.Id,
                result.ProviderMessageId,
                ct
            );
        else
            await NotificationsEmailSendRequestedConsumer.PublishFailedAsync(
                bus,
                evt,
                message.Id,
                result.ProviderMessageId,
                result.ErrorReason ?? command.FallbackFailureReason,
                now,
                ct
            );
    }
}
