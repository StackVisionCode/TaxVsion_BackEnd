namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Datos del hecho de negocio que no viven en el catálogo y que el dispatcher necesita
/// para publicar <c>notification.inapp.requested.v1</c> hacia Communication. Opcional:
/// sin esto, <c>RecordInAppAsync</c> solo persiste el log de auditoría (consumers legacy).
/// </summary>
public sealed record InAppNotificationRequest(
    string Kind,
    Guid RecipientUserId,
    Guid SourceEventId,
    string SourceEventType,
    DateTime OccurredAtUtc,
    string FallbackTitle,
    string? FallbackBody = null,
    Guid? ActorUserId = null,
    string? ActorDisplayName = null,
    string? ResourceId = null,
    IReadOnlyDictionary<string, string>? ResourceContext = null,
    IReadOnlyDictionary<string, string>? Params = null,
    string? GroupKey = null
);
