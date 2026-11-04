using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.EmailIntegrationEvents;

/// <summary>
/// Resultado de entrega ya correlacionado de un correo enviado desde el CRM (Correspondence). Postmaster
/// es dueño de la correlación (Message-ID + destinatario); Correspondence solo lo refleja en la
/// conversación. Uno por destinatario y por cambio de resultado.
/// </summary>
[MessageIdentity("postmaster.correspondence_delivery_feedback.v1")]
public sealed record PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent : IntegrationEvent
{
    public required Guid SentMessageId { get; init; }
    public required Guid CorrespondenceDraftId { get; init; }
    public required string Recipient { get; init; }

    /// <summary>Ver <see cref="CorrespondenceDeliveryOutcomes"/>.</summary>
    public required string Outcome { get; init; }

    /// <summary>Motivo en categorías estables. Ver <see cref="DeliveryFailureCategories"/>; null si se entregó.</summary>
    public string? Category { get; init; }

    /// <summary>Código RFC 3463, ej. "5.1.1".</summary>
    public string? StatusCode { get; init; }

    /// <summary>Respuesta técnica del servidor, truncada (soporte). La UI muestra el motivo de la categoría.</summary>
    public string? DiagnosticSnippet { get; init; }

    /// <summary>La dirección quedó en la lista de supresión por este resultado.</summary>
    public required bool Suppressed { get; init; }

    /// <summary>Buzón donde llegó el aviso y su id ahí, para "ver aviso original" en vivo. Null si vino por webhook.</summary>
    public Guid? ReportAccountId { get; init; }
    public string? ReportProviderMessageId { get; init; }

    public required DateTime EventAtUtc { get; init; }
}

/// <summary>Resultado de entrega de un correo del CRM (contrato estable entre servicios y con el front).</summary>
public static class CorrespondenceDeliveryOutcomes
{
    public const string Delivered = "Delivered";
    public const string Delayed = "Delayed";
    public const string NotDelivered = "NotDelivered";
}

/// <summary>Categorías del motivo de una no entrega, derivadas del código de estado (RFC 3463).</summary>
public static class DeliveryFailureCategories
{
    public const string InvalidAddress = "InvalidAddress";
    public const string DomainNotFound = "DomainNotFound";
    public const string MailboxFull = "MailboxFull";
    public const string Rejected = "Rejected";
    public const string TooLarge = "TooLarge";
    public const string Delayed = "Delayed";
    public const string Other = "Other";
}
