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
    ILogger<InAppDispatch> logger
)
{
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
            correlationId
        );
        if (logResult.IsFailure)
            return;

        logResult.Value.MarkSent();
        await logs.AddAsync(logResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        if (TryBuildEvent(tenantId, correlationId, inApp, out var evt))
            await publisher.PublishAsync(evt!, ct);

        // InApp considerado "sent" al publicar el integration event — el consumer Node persistirá la
        // fila y emitirá el socket. Un fallo real de Node queda fuera del scope de este contador
        // (sería un contador propio en Communication).
        metrics.RecordSent(inAppKind, "InApp");
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
