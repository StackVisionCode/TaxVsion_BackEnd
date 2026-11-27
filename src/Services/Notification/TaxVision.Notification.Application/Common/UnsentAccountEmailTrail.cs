using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// IA-02: un reset que agotó los reintentos sin subdominio de la oficina no llegó a encolarse. Deja una fila Failed
/// con el EventId para que el panel de la oficina lo vea (04 §4.3). La plantilla propia no bloquea una reposición
/// manual del mensaje: el envío real tendría su propia fila, más nueva.
/// </summary>
public static class UnsentAccountEmailTrail
{
    public const string TemplateKey = "auth.password_reset.unsent";
    public const string Reason = "tenant_host_unresolved";

    public static async Task<bool> RecordAsync(
        object? message,
        Exception failure,
        INotificationLogRepository logs,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (message is not PasswordResetRequestedIntegrationEvent evt || failure is not TenantHostUnavailableException)
            return false;

        var recipient = NotificationRecipient.ForEmail(evt.Email);
        if (recipient.IsFailure)
            return false;
        var log = NotificationLog.Create(
            evt.TenantId,
            NotificationChannel.Email,
            recipient.Value,
            "Not sent: the office address could not be resolved.",
            TemplateKey,
            evt.EventId,
            evt.CorrelationId
        );
        if (log.IsFailure)
            return false;

        log.Value.MarkFailed(Reason);
        await logs.AddAsync(log.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
