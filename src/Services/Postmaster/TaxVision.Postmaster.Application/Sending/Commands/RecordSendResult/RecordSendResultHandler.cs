using BuildingBlocks.Persistence;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;

/// <summary>
/// Estado del mensaje y cierre de la reserva en la misma transacción. El aviso a Notification lo levanta el agregado
/// en la transición (Sent/Failed): sale una sola vez, después del commit.
/// </summary>
public static class RecordSendResultHandler
{
    public static async Task Handle(
        RecordSendResultCommand command,
        ISentMessageRepository sentMessages,
        IIdempotencyGuard idempotencyGuard,
        IUnitOfWork unitOfWork,
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

        SendResultRecorder.Record(message, command.Result, command.FallbackFailureReason, DateTime.UtcNow);
        await idempotencyGuard.CompleteAsync(message.TenantId, message.IdempotencyKey, message.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
