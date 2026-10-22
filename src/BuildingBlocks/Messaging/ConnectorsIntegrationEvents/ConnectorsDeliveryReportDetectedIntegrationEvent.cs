using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.ConnectorsIntegrationEvents;

/// <summary>
/// Llegó al buzón conectado un aviso de no entrega (DSN/NDR) de un correo enviado desde ese buzón.
/// Uno por destinatario del informe. Los campos son del protocolo (RFC 3464); qué significan para el
/// envío lo decide Postmaster. El aviso igual sigue apareciendo en la bandeja del usuario.
/// </summary>
[MessageIdentity("connectors.delivery_report_detected.v1")]
public sealed record ConnectorsDeliveryReportDetectedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }

    /// <summary>Id del aviso en el buzón: dos lecturas del mismo aviso dan el mismo id.</summary>
    public required string ReportProviderMessageId { get; init; }

    public required string OriginalInternetMessageId { get; init; }
    public required string Recipient { get; init; }

    /// <summary>failed, delayed o delivered.</summary>
    public required string Action { get; init; }

    /// <summary>Código RFC 3463, ej. "5.1.1".</summary>
    public string? StatusCode { get; init; }

    /// <summary>Respuesta del servidor que rebotó, truncada. Nunca contenido del correo.</summary>
    public string? DiagnosticSnippet { get; init; }

    public required DateTime ReportedAtUtc { get; init; }
}
