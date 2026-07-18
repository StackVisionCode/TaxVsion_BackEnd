using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.EmailIntegrationEvents;

// -----------------------------------------------------------------------------
// Contratos Notifications ↔ Postmaster (Notifications_Service_Responsibility_Cleanup_Plan §19).
// Introducidos en Notifications Fase 4, publicados bajo el feature flag
// Notification:UsePostmasterDispatch — desde Hardening Fase 21 (2026-07-18) ese flag es true por
// default, así que este es el path real salvo rollback explícito. El gateway InProcess (y su
// equivalente EmailDeliveryService, Fase 19) siguen vivos como fallback; retirarlos es trabajo
// futuro fuera del plan de hardening.
// -----------------------------------------------------------------------------

/// <summary>
/// Notification pide a Postmaster despachar un email transaccional. Reemplaza al viejo
/// <see cref="EmailSendRequestedIntegrationEvent"/> (que era interno del monolito Notification).
/// </summary>
/// <remarks>
/// El cuerpo del correo VIAJA en el evento (a diferencia del legacy que solo llevaba MessageId).
/// Notification no persiste OutboundEmailMessage — la responsabilidad material vive en Postmaster.
/// El <c>NotificationLogId</c> es la clave opaca de correlación que Postmaster devuelve en los
/// callbacks para que Notification actualice el estado de su <c>NotificationDispatchAttempt</c>.
/// </remarks>
[MessageIdentity("notifications.email_send_requested.v1")]
public sealed record NotificationsEmailSendRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationLogId { get; init; }
    public required Guid DispatchAttemptId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string To { get; init; }
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public required string TextBody { get; init; }
    public required string TemplateKey { get; init; }

    /// <summary>System = credenciales SMTP de TaxVision. Tenant = SMTP del tenant. Ver plan §14.5.</summary>
    public required string RequiredProviderScope { get; init; }

    /// <summary>System = logo TaxVision. Tenant = logo del tenant. Ver plan §14.5.</summary>
    public required string LogoScope { get; init; }

    /// <summary>Transactional = envío inmediato. Bulk = cola de campañas (Fase futura).</summary>
    public required string Stream { get; init; }

    public IReadOnlyList<string>? Cc { get; init; }
    public IReadOnlyList<string>? Bcc { get; init; }
    public IReadOnlyDictionary<string, string>? TemplateVariables { get; init; }
    public string? PriorityHint { get; init; }
    public Guid? ReplyToThreadId { get; init; }
    public IReadOnlyList<Guid>? AttachmentFileIds { get; init; }

    /// <summary>
    /// Logos/imágenes inline que Scribe resolvió al renderizar este email (Scribe Fase 4.5). Viaja
    /// como REFERENCIA, no como bytes — Postmaster ya tiene <c>IInlineAssetFetcher</c> (Fase 3.5)
    /// para descargar el contenido real de CloudStorage justo antes de armar el MIME; cargar bytes
    /// acá infla el tamaño del mensaje en la outbox/inbox de Wolverine sin necesidad.
    ///
    /// Hardening Fase 9: antes de esta fase, <see cref="ScribeRenderedEmail"/> descartaba este dato
    /// al deserializar la respuesta de Scribe, así que ningún logo llegaba nunca hasta acá — el
    /// pipeline CID completo (Scribe → Notification → Postmaster → SMTP) estaba construido pieza por
    /// pieza pero nunca conectado de punta a punta. Este campo es el eslabón que faltaba.
    /// </summary>
    public IReadOnlyList<EmailInlineAssetReference>? InlineAssets { get; init; }
}

/// <summary>
/// Referencia (no bytes) a un asset embebido por Content-ID — mismo shape que
/// <c>TaxVision.Scribe.Application.Rendering.InlineAsset</c> (origen del dato) y
/// <c>TaxVision.Postmaster.Domain.Sending.InlineAsset</c> (destino, valida el mismo shape vía su
/// factory <c>Create</c>). Vive en BuildingBlocks porque cruza los tres servicios del pipeline de
/// logos: es el tipo de <c>ScribeRenderedEmail.InlineAssets</c> (DTO interno de Notification) y de
/// <see cref="NotificationsEmailSendRequestedIntegrationEvent.InlineAssets"/> (evento sobre el wire).
/// </summary>
public sealed record EmailInlineAssetReference(
    string ContentId,
    Guid CloudStorageFileId,
    string ContentType,
    long SizeBytes
);

/// <summary>Postmaster confirma que entregó el email al MTA con éxito.</summary>
[MessageIdentity("postmaster.email_delivery.succeeded.v1")]
public sealed record PostmasterEmailDeliverySucceededIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationLogId { get; init; }
    public required Guid DispatchAttemptId { get; init; }

    /// <summary>Id opaco del envío material en Postmaster (SentMessage.Id).</summary>
    public required Guid SentMessageId { get; init; }

    /// <summary>Id devuelto por el proveedor SMTP (message-id header, útil para tracking).</summary>
    public string? ProviderMessageId { get; init; }

    public required DateTime EventAtUtc { get; init; }
}

/// <summary>Postmaster falló el envío tras agotar reintentos.</summary>
[MessageIdentity("postmaster.email_delivery.failed.v1")]
public sealed record PostmasterEmailDeliveryFailedIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationLogId { get; init; }
    public required Guid DispatchAttemptId { get; init; }
    public required Guid SentMessageId { get; init; }
    public string? ProviderMessageId { get; init; }
    public required string Reason { get; init; }
    public required DateTime EventAtUtc { get; init; }
}

/// <summary>Bounce detectado por webhook (permanent/soft) sobre un envío ya Sent.</summary>
[MessageIdentity("postmaster.email_delivery.bounced.v1")]
public sealed record PostmasterEmailDeliveryBouncedIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationLogId { get; init; }
    public required Guid DispatchAttemptId { get; init; }
    public required Guid SentMessageId { get; init; }
    public string? ProviderMessageId { get; init; }
    public required string BounceType { get; init; }
    public required string Reason { get; init; }
    public required DateTime EventAtUtc { get; init; }
}

/// <summary>El destinatario estaba en la suppression list de Postmaster; el envío no se intentó.</summary>
[MessageIdentity("postmaster.email_delivery.suppressed.v1")]
public sealed record PostmasterEmailDeliverySuppressedIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationLogId { get; init; }
    public required Guid DispatchAttemptId { get; init; }
    public required Guid SentMessageId { get; init; }
    public required string SuppressionReason { get; init; }
    public required DateTime EventAtUtc { get; init; }
}

/// <summary>
/// La notificación era tenant-scoped y el tenant NO tiene TenantEmailProvider configurado.
/// Postmaster no intentó enviar. Notification puede escalar in-app al tenant admin con CTA
/// "Configurá tu SMTP" (plan §17, §19).
/// </summary>
[MessageIdentity("postmaster.email_delivery.provider_not_configured.v1")]
public sealed record PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationLogId { get; init; }
    public required Guid DispatchAttemptId { get; init; }
    public required DateTime EventAtUtc { get; init; }
}
