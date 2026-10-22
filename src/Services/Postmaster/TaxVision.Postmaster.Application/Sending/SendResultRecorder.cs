using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending;

/// <summary>
/// Pasa el resultado del envío al <see cref="SentMessage"/>. Lo comparten las dos vías (SMTP y buzón)
/// para que un destinatario rechazado por el relay no quede marcado como enviado.
/// </summary>
internal static class SendResultRecorder
{
    public static void Record(SentMessage message, SendResult sendResult, string fallbackFailureReason, DateTime nowUtc)
    {
        if (!sendResult.Success)
        {
            message.MarkAsFailed(sendResult.ErrorReason ?? fallbackFailureReason, nowUtc);
            return;
        }

        foreach (var outcome in sendResult.RecipientOutcomes)
        {
            if (outcome.Status == RecipientSendStatus.Rejected)
                message.MarkRecipientRejectedAtSend(
                    outcome.RecipientId,
                    outcome.ErrorReason ?? "Rejected by the relay.",
                    nowUtc
                );
        }

        // El id del proveedor sirve para correlacionar sus avisos; si no vino, queda el Message-ID.
        message.MarkAsSent(
            sendResult.ProviderReference ?? sendResult.ProviderMessageId,
            nowUtc,
            sendResult.ProviderThreadId
        );
        if (sendResult.ProviderAcceptance is not null)
            message.RecordProviderAcceptance(sendResult.ProviderAcceptance);
        if (sendResult.InternetMessageId is not null)
            message.RecordInternetMessageId(sendResult.InternetMessageId);
    }
}
