using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;
using Wolverine;

namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Avisa que cambió el estado visible de un hilo (no leídos, archivado, mensajes) para que el CRM lo
/// refleje en vivo en otras pestañas y empleados. Se llama DESPUÉS de guardar: el conteo sale de la base.
/// </summary>
internal static class ThreadStateNotifier
{
    public static async Task PublishAsync(
        IMessageBus bus,
        IIncomingEmailRepository incomingEmails,
        EmailThread thread,
        Guid? actorUserId,
        string correlationId,
        CancellationToken ct
    )
    {
        var unread = await incomingEmails.GetUnreadCountsByThreadAsync(thread.TenantId, [thread.Id], ct);
        await bus.PublishAsync(
            new CorrespondenceThreadStateChangedIntegrationEvent
            {
                TenantId = thread.TenantId,
                CorrelationId = correlationId,
                EmailThreadId = thread.Id,
                CustomerId = thread.CustomerId,
                Status = thread.Status.ToString(),
                UnreadCount = unread.GetValueOrDefault(thread.Id),
                MessageCount = thread.MessageCount,
                ActorUserId = actorUserId,
            }
        );
    }
}
