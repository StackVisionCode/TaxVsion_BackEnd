using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using Wolverine.Attributes;

namespace TaxVision.Notification.Application.Push.Consumers;

/// <summary>
/// N8-04: la llamada a FCM, fuera de la transacción del consumer que pidió el push. Deduplica por evento, kind y
/// destinatario justo antes de enviar; un fallo transitorio deja el log Failed y se reintenta.
/// </summary>
public static class DeliverPushConsumer
{
    // El intento y el resultado se guardan por su cuenta (PushAttemptRecorder).
    [NonTransactional]
    public static async Task Handle(
        DeliverPush command,
        NotificationDispatcher dispatcher,
        ILogger<DeliverPush> logger,
        CancellationToken ct
    )
    {
        var outcome = await dispatcher.DispatchPushAsync(command.Request, ct);
        if (outcome.Error is not { } error)
            return;
        if (PushErrorCodes.IsTransient(error.Code))
            throw new InvalidOperationException(error.Code);
        logger.LogWarning("Push to user {UserId} was not sent: {Code}", command.Request.RecipientUserId, error.Code);
    }
}
