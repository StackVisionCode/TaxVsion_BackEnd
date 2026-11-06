using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Inbox.DomainEvents;

/// <summary>
/// Avisa que cambió el estado visible de un hilo (no leídos, archivado, mensajes) para que el CRM lo refleje en vivo
/// en otras pestañas y empleados. Corre después del commit y lee de la base, con el tenant explícito: si llegaron
/// dos cambios seguidos, el último aviso trae lo último que se confirmó.
/// </summary>
public static class EmailThreadStateChangedDomainEventHandler
{
    public static async Task<CorrespondenceThreadStateChangedIntegrationEvent?> Handle(
        EmailThreadStateChangedDomainEvent changed,
        IEmailThreadRepository threads,
        IIncomingEmailRepository incomingEmails,
        CancellationToken ct
    )
    {
        var thread = await threads.GetByIdAsync(changed.TenantId, changed.EmailThreadId, ct);
        if (thread is null)
            return null;

        var unread = await incomingEmails.GetUnreadCountsByThreadAsync(changed.TenantId, [thread.Id], ct);
        return new CorrespondenceThreadStateChangedIntegrationEvent
        {
            TenantId = changed.TenantId,
            CorrelationId = changed.CorrelationId,
            EmailThreadId = thread.Id,
            CustomerId = thread.CustomerId,
            Status = thread.Status.ToString(),
            UnreadCount = unread.GetValueOrDefault(thread.Id),
            MessageCount = thread.MessageCount,
            ActorUserId = changed.ActorUserId,
        };
    }
}
