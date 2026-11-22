using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// "Este kind, para este destinatario, con este contenido". Canales, categoría de preferencia y obligatoriedad
/// salen del catálogo (<see cref="NotificationDispatcher.DispatchAsync"/>); solo se intenta un canal con contenido.
/// </summary>
public sealed record NotificationIntent(
    string Kind,
    Guid TenantId,
    NotificationTarget Target,
    Guid SourceEventId,
    string SourceEventType,
    DateTime OccurredAtUtc,
    string? CorrelationId,
    InAppContent? InApp = null,
    PushContent? Push = null,
    EmailContent? Email = null,
    SmsContent? Sms = null
);

/// <summary>
/// A quién va. In-app y push necesitan usuario (un in-app sin usuario y con correo deja solo la fila de auditoría a
/// nombre del invitado); correo, dirección; SMS, teléfono.
/// </summary>
public sealed record NotificationTarget(
    Guid? UserId = null,
    string? Email = null,
    string? Phone = null,
    Guid? CustomerId = null,
    // Idioma ya resuelto por el consumer (caché del evento); sin él se busca.
    RecipientLocale? Locale = null
);

/// <summary>El texto de respaldo sale del archivo de textos con <see cref="Params"/>.</summary>
public sealed record InAppContent(
    IReadOnlyDictionary<string, string>? Params = null,
    string? ResourceId = null,
    IReadOnlyDictionary<string, string>? ResourceContext = null,
    string? GroupKey = null,
    Guid? ActorUserId = null,
    string? ActorDisplayName = null,
    // Clave del log; por defecto el kind. No cambiarla al migrar un consumer: deduplica.
    string? TemplateKey = null,
    // Respaldo propio del consumer (asunto del log incluido); sin él, el del archivo de textos.
    string? FallbackTitle = null,
    string? FallbackBody = null
);

/// <summary>El texto sale del archivo de textos en el idioma del destinatario (genérico, sin datos personales).</summary>
public sealed record PushContent(
    string? ResourceId = null,
    Guid? CustomerId = null,
    string? Tag = null,
    TimeSpan? TimeToLive = null,
    IReadOnlyDictionary<string, string>? Data = null,
    // Enlace al portal (cuenta de cliente) en vez del CRM.
    bool ForClient = false,
    // Variante del texto (p. ej. llamada con video); por defecto el kind.
    string? TextKey = null,
    // Evento que el clic busca en la bandeja; por defecto el de la intención si lleva in-app.
    Guid? SourceEventId = null,
    // Sin dispositivos no se escribe nada (ni log ni error).
    bool SkipWhenNoDevices = false,
    // Enlace al CRM o al portal según la cuenta del destinatario (chat y llamadas).
    bool LinkForRecipient = false,
    // Sin enlace: no hay a dónde llevar el clic.
    bool WithoutLink = false,
    // Texto propio del consumer; sin él, el del archivo de textos.
    string? Title = null,
    string? Body = null,
    // Enlace directo (lleva parámetros que /n/open no transporta).
    string? Link = null,
    // Envío en el handler, sin cola de push (llamada entrante: caduca y su reintento es inmediato).
    bool Inline = false
);

/// <summary>Plantilla de Scribe; se renderiza en el idioma del destinatario.</summary>
public sealed record EmailContent(
    string EventKey,
    IReadOnlyDictionary<string, object?> Variables,
    ScribeRenderAudience Audience = ScribeRenderAudience.Staff,
    // Clave del log; por defecto la de Scribe.
    string? TemplateKey = null,
    bool Confidential = false,
    string? IdempotencyKey = null,
    // Render propio del consumer (compartido por idioma, por audiencia o por persona); con él se ignoran
    // Variables y Audience.
    Func<RecipientLocale, CancellationToken, Task<ScribeRenderedEmail>>? Render = null,
    // Adjuntos generados en memoria (el .ics de una reunión).
    IReadOnlyList<EmailInlineAttachment>? InlineAttachments = null,
    // Archivos de CloudStorage que van adjuntos (el PDF de una factura).
    IReadOnlyList<Guid>? AttachmentFileIds = null
);

/// <summary>Clave del texto en la sección <c>sms</c> del archivo de textos.</summary>
public sealed record SmsContent(
    string TextKey,
    string TemplateKey,
    IReadOnlyDictionary<string, string>? Params = null,
    string? IdempotencyToken = null
);

/// <param name="Errors">El error de cada canal que falló, si lo hay (p. ej. para reintentar un push transitorio).</param>
public sealed record DispatchSummary(
    IReadOnlyDictionary<NotificationChannel, ChannelOutcome> Channels,
    IReadOnlyDictionary<NotificationChannel, Error>? Errors = null
);

public enum ChannelOutcome
{
    Sent,
    Queued,
    NotRequested,
    NotAllowedByCatalog,
    SuppressedByPreference,
    RecipientInactive,
    NoDestination,
    Failed,
}
