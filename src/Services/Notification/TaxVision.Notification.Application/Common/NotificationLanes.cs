using BuildingBlocks.Messaging.CampaignsIntegrationEvents;

namespace TaxVision.Notification.Application.Common;

/// <summary>Colas locales durables de Notification (D-N44 opción A). Lo crítico se queda en notification-events.</summary>
public static class NotificationLanes
{
    /// <summary>Chat y campañas (N8-03).</summary>
    public const string Bulk = "notification-bulk";

    /// <summary>Envío a FCM por destinatario, sin transacción (N8-04).</summary>
    public const string Push = "notification-push";
}

/// <summary>Push de un mensaje de chat, ya fuera de notification-events.</summary>
public sealed record DispatchChatPush(
    Guid TenantId,
    Guid EventId,
    Guid ConversationId,
    Guid MessageId,
    Guid SenderId,
    IReadOnlyList<Guid> RecipientUserIds,
    // Null = evento viejo sin lista: se avisa a todos.
    IReadOnlyList<Guid>? PushRecipientUserIds,
    DateTime SentAtUtc,
    string? CorrelationId
);

/// <summary>Un envío de campaña (correo o push), ya fuera de notification-events.</summary>
public sealed record DispatchCampaignMessage(Guid TenantId, CampaignDispatchRequestedIntegrationEvent Request);

/// <summary>Un push ya aprobado por el gate; lo envía <c>DeliverPushConsumer</c>.</summary>
public sealed record DeliverPush(Guid TenantId, PushDispatchRequest Request);
