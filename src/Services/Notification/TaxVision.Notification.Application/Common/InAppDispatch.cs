using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <summary>Canal in-app: registra el log y pide a Communication la fila y el socket por integration event.</summary>
public sealed class InAppDispatch(
    INotificationLogRepository logs,
    DispatchChannelGate gate,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    INotificationDispatchMetrics metrics,
    IUserEmailDirectoryRepository directory,
    ICustomerPortalAccountProjectionRepository portalAccounts,
    ILogger<InAppDispatch> logger
)
{
    // Nombre del actor por evento: con muchos destinatarios se busca una sola vez.
    private readonly Dictionary<(Guid TenantId, Guid UserId), string?> actorNames = [];

    /// <summary>
    /// Registra una notificación in-app (sin envío externo). <paramref name="recipientUserId"/>
    /// es opcional porque <paramref name="recipient"/> a veces no es un usuario real (ej. un
    /// invitado a un meeting por email, sin cuenta) — sin un UserId no hay preferencia que
    /// consultar, así que se despacha igual (mismo comportamiento que antes de esta fase).
    /// </summary>
    public async Task RecordAsync(
        Guid tenantId,
        NotificationRecipient recipient,
        string subject,
        NotificationCategory category,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId,
        Guid? recipientUserId = null,
        InAppNotificationRequest? inApp = null,
        CancellationToken ct = default
    )
    {
        var inAppKind = inApp?.Kind ?? templateKey;
        var activeUserId = recipientUserId ?? inApp?.RecipientUserId;
        if (
            !await gate.AllowsAsync(
                tenantId,
                activeUserId,
                recipientUserId,
                category,
                NotificationChannel.InApp,
                inAppKind,
                ct
            )
        )
            return;

        var logResult = NotificationLog.Create(
            tenantId,
            NotificationChannel.InApp,
            recipient,
            subject,
            templateKey,
            relatedEventId,
            correlationId,
            // El kind del catálogo: con él se encuentra la fila si Communication la rechaza.
            kind: inApp?.Kind
        );
        if (logResult.IsFailure)
            return;

        if (inApp is not null)
            inApp = await WithActorNameAsync(tenantId, inApp, ct);
        var published = TryBuildEvent(tenantId, correlationId, inApp, out var evt);
        // Sin entrada en el catálogo no se publica nada: la fila no puede decir Sent.
        if (inApp is not null && !published)
            logResult.Value.MarkFailed(KindNotInCatalog);
        else
            logResult.Value.MarkSent();
        await logs.AddAsync(logResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        if (published)
            await publisher.PublishAsync(evt!, ct);

        if (inApp is not null && !published)
        {
            metrics.RecordFailed(inAppKind, "InApp");
            return;
        }

        // InApp considerado "sent" al publicar el integration event — el consumer Node persistirá la
        // fila y emitirá el socket. Si la rechaza, avisa y la fila pasa a Failed (InAppRejectedConsumer).
        metrics.RecordSent(inAppKind, "InApp");
    }

    public const string KindNotInCatalog = "kind_not_in_catalog";

    // NF5: el staff ve quién hizo la acción (nombre del directorio); el portal nunca recibe nombres.
    private async Task<InAppNotificationRequest> WithActorNameAsync(
        Guid tenantId,
        InAppNotificationRequest inApp,
        CancellationToken ct
    )
    {
        if (!NotificationCatalog.TryGet(inApp.Kind, out var entry) || entry is null)
            return inApp;
        if (
            entry.RecipientPolicy == NotificationRecipientPolicy.CustomerPortal
            || entry.Category == NotificationCatalogCategory.Portal
        )
            return inApp with { ActorDisplayName = null };
        if (inApp.ActorUserId is null && string.IsNullOrWhiteSpace(inApp.ActorDisplayName))
            return inApp;
        // Kinds compartidos (llamada perdida, mensajes perdidos): decide quién lo recibe.
        if (await portalAccounts.GetByUserAsync(tenantId, inApp.RecipientUserId, ct) is not null)
            return inApp with { ActorDisplayName = null };
        if (inApp.ActorUserId is not { } actorId || !string.IsNullOrWhiteSpace(inApp.ActorDisplayName))
            return inApp;

        if (!actorNames.TryGetValue((tenantId, actorId), out var name))
            actorNames[(tenantId, actorId)] = name = (await directory.FindAsync(tenantId, actorId, ct))?.DisplayName;
        return string.IsNullOrWhiteSpace(name) ? inApp : inApp with { ActorDisplayName = name };
    }

    private bool TryBuildEvent(
        Guid tenantId,
        string? correlationId,
        InAppNotificationRequest? inApp,
        out InAppNotificationRequestedIntegrationEvent? evt
    )
    {
        evt = null;
        if (inApp is null)
            return false;
        if (!NotificationCatalog.TryGet(inApp.Kind, out var entry) || entry is null)
        {
            logger.LogWarning("In-app notification kind {Kind} is not in the catalog; nothing published.", inApp.Kind);
            return false;
        }

        // Trunca antes de publicar: el consumer Node acepta hasta estos limites
        // tras trim; pasar un string mas largo o un cuerpo vacio mandaba la fila
        // directo a DLQ y se perdia la notificacion.
        var fallbackBody = NormalizeOrNull(inApp.FallbackBody, maxLength: 1000);
        evt = new InAppNotificationRequestedIntegrationEvent
        {
            TenantId = tenantId,
            CorrelationId = Truncate(correlationId, 64) ?? string.Empty,
            OccurredOn = DateTime.UtcNow,
            RecipientUserId = inApp.RecipientUserId,
            Category = NotificationContractNames.For(entry.Category),
            Kind = Truncate(entry.Kind, 64) ?? entry.Kind,
            Priority = NotificationContractNames.For(entry.DefaultPriority),
            TitleKey = Truncate(entry.TitleKey, 160) ?? entry.TitleKey,
            BodyKey = Truncate(entry.BodyKey, 160),
            Params = new Dictionary<string, string>(inApp.Params ?? EmptyMap),
            FallbackTitle = Truncate(inApp.FallbackTitle, 200) ?? inApp.FallbackTitle,
            FallbackBody = fallbackBody,
            ActorUserId = inApp.ActorUserId,
            ActorDisplayName = Truncate(inApp.ActorDisplayName, 160),
            ResourceType = NotificationContractNames.For(entry.ResourceType),
            ResourceId = Truncate(inApp.ResourceId, 128),
            ResourceContext = new Dictionary<string, string>(inApp.ResourceContext ?? EmptyMap),
            Action = NotificationContractNames.For(entry.Action),
            SourceEventId = inApp.SourceEventId,
            SourceEventType = Truncate(inApp.SourceEventType, 128) ?? inApp.SourceEventType,
            GroupKey = Truncate(inApp.GroupKey, 256),
            OccurredAtUtc = DateTime.SpecifyKind(inApp.OccurredAtUtc, DateTimeKind.Utc),
        };
        return true;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyMap = new Dictionary<string, string>();

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];

    // Cuerpo vacio tras trim equivale a "no body": el consumer caera a fallbackTitle
    // y Notification.create no fallara por MissingBody.
    private static string? NormalizeOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
