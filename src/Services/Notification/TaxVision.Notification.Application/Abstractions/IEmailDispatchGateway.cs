using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Punto único abstracto de invocación para despachar un email transaccional desde un consumer.
/// Introducido en Notifications Fase 3 para separar el sitio de la invocación de la implementación
/// material del envío. En Fase 3 la única implementación es <c>InProcessEmailDispatchGateway</c> —
/// hace lo mismo que <c>NotificationDispatcher.SendEmailAsync</c> hoy pero centralizando la creación
/// del <see cref="NotificationDispatchAttempt"/>. En Fase 4 se agrega
/// <c>EventBasedEmailDispatchGateway</c> que publica <c>notifications.email_send_requested.v1</c>
/// hacia Postmaster; los consumers no se enteran del cambio.
/// </summary>
public interface IEmailDispatchGateway
{
    Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default);
}

/// <summary>
/// Request completo para despachar un email. El gateway crea <see cref="NotificationLog"/> +
/// <see cref="NotificationDispatchAttempt"/> internamente — el consumer no necesita crearlos.
/// Los campos <c>TemplateVariables</c>, <c>Cc</c>, <c>Bcc</c>, <c>AttachmentFileIds</c>,
/// <c>ReplyToThreadId</c>, <c>Scope</c>, <c>Stream</c>, <c>PriorityHint</c> están reservados para
/// Fase 4 (evento hacia Postmaster) y NO se usan hoy — la implementación in-process solo lee To,
/// Subject, HtmlBody, TextBody, TemplateKey, RelatedEventId, CorrelationId, TenantId.
/// </summary>
/// <param name="InlineAssets">
/// Logos/imágenes CID resueltas por Scribe para este render (Hardening Fase 9) — el consumer las
/// obtiene de <c>ScribeRenderedEmail.InlineAssets</c> y las reenvía tal cual. Solo
/// <c>EventBasedEmailDispatchGateway</c> las usa hoy (las propaga al evento hacia Postmaster);
/// <c>InProcessEmailDispatchGateway</c> las ignora — el path directo por SMTP nunca tuvo soporte de
/// logos inline y no es parte de esta fase.
/// </param>
public sealed record EmailDispatchRequest(
    Guid TenantId,
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string TemplateKey,
    Guid? RelatedEventId,
    string? CorrelationId,
    string? IdempotencyKey = null,
    IReadOnlyList<string>? Cc = null,
    IReadOnlyList<string>? Bcc = null,
    IReadOnlyDictionary<string, object>? TemplateVariables = null,
    EmailDispatchScope Scope = EmailDispatchScope.System,
    EmailDispatchStream Stream = EmailDispatchStream.Transactional,
    string? PriorityHint = null,
    Guid? ReplyToThreadId = null,
    IReadOnlyList<Guid>? AttachmentFileIds = null,
    IReadOnlyList<EmailInlineAssetReference>? InlineAssets = null
);

public sealed record EmailDispatchResult(
    Guid NotificationLogId,
    Guid DispatchAttemptId,
    NotificationDispatchAttemptStatus Status,
    string? ProviderMessageId,
    string? Error
)
{
    public bool IsSuccess =>
        Status is NotificationDispatchAttemptStatus.Sent or NotificationDispatchAttemptStatus.Queued;
}

public enum EmailDispatchScope
{
    System,
    Tenant,
}

public enum EmailDispatchStream
{
    Transactional,
    Bulk,
}
