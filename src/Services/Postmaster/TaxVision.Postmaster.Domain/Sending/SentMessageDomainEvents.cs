using BuildingBlocks.Domain;

namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>A quién avisar el resultado. Null: el correo no lo pidió Notification (Correspondence lee la respuesta HTTP).</summary>
public sealed record SentMessageCallback(
    Guid NotificationLogId,
    Guid DispatchAttemptId,
    Guid? CampaignId,
    string? CorrelationId
);

/// <summary>El proveedor aceptó el correo.</summary>
public sealed record SentMessageSentDomainEvent(
    Guid TenantId,
    Guid SentMessageId,
    SentMessageCallback? Callback,
    string? ProviderMessageId,
    DateTime OccurredAtUtc
) : IDomainEvent;

/// <summary>El correo no salió, o el resultado quedó desconocido.</summary>
public sealed record SentMessageFailedDomainEvent(
    Guid TenantId,
    Guid SentMessageId,
    SentMessageCallback? Callback,
    string? ProviderMessageId,
    string Reason,
    DateTime OccurredAtUtc
) : IDomainEvent;

/// <summary>Todos los destinatarios estaban en la lista de supresión: nunca se intentó enviar.</summary>
public sealed record SentMessageSuppressedDomainEvent(
    Guid TenantId,
    Guid SentMessageId,
    SentMessageCallback? Callback,
    string Reason,
    DateTime OccurredAtUtc
) : IDomainEvent;
