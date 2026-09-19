using TaxVision.Sms.Domain.Messages;

namespace TaxVision.Sms.Application.Messages.Queries;

/// <summary>
/// Filtro de estado para el listado de mensajes. <see cref="All"/> no filtra; el resto mapea 1:1 a
/// <see cref="SmsMessageStatus"/>. Se recibe como string en el query param y se parsea aquí.
/// </summary>
public enum SmsMessageStatusFilter
{
    All = 0,
    Pending,
    Accepted,
    Delivered,
    Failed,
    Undeliverable,
    Suppressed,
}

/// <summary>Fila del historial de SMS. NO expone datos de infraestructura (proveedor, id de proveedor):
/// el CRM solo necesita a quién, qué, cuándo y en qué estado. El motivo de fallo viaja como código
/// estable (<see cref="FailureCode"/>) para que el front lo traduzca a lenguaje simple.</summary>
public sealed record SmsMessageSummaryResponse(
    Guid Id,
    Guid CustomerId,
    string To,
    string Body,
    SmsMessageStatus Status,
    string? FailureCode,
    bool HasMedia,
    DateTime CreatedAtUtc
);

/// <summary>Media adjunta (MMS) — solo metadatos, nunca el binario.</summary>
public sealed record SmsMediaResponse(string Url, string ContentType, string? FileName, long? SizeBytes);

/// <summary>Detalle de un mensaje con su línea de tiempo de estado (timestamps) y su media.</summary>
public sealed record SmsMessageDetailResponse(
    Guid Id,
    Guid CustomerId,
    string To,
    string Body,
    SmsMessageStatus Status,
    string? FailureCode,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? FailedAtUtc,
    IReadOnlyList<SmsMediaResponse> Media
);

/// <summary>Conteos agregados por estado en una ventana (por <c>CreatedAtUtc</c>). El front deriva la
/// tasa de entrega. <see cref="OptedOut"/> es el total de bajas vigentes del tenant (no de la ventana).</summary>
public sealed record SmsStatsResponse(
    int Total,
    int Pending,
    int Accepted,
    int Delivered,
    int Failed,
    int Undeliverable,
    int Suppressed,
    int OptedOut
);
