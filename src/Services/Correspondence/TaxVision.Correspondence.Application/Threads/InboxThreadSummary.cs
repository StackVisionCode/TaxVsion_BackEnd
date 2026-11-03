namespace TaxVision.Correspondence.Application.Threads;

/// <summary>
/// Fila del inbox de la oficina: el hilo, su cliente y lo necesario para decidir sin abrirlo (último
/// remitente entrante, vista previa, adjuntos y no leídos). Remitente y vista previa son null si el hilo
/// solo tiene enviados. <see cref="CustomerId"/> null = "Not a client" (hilo nacido de un aviso de no entrega).
/// <see cref="LastMessageAtUtc"/> es la última actividad (un aviso de no entrega la sube);
/// <see cref="LatestDeliveryNotice"/> es el aviso de entrega más reciente, null si no hay.
/// </summary>
public sealed record InboxThreadSummary(
    Guid ThreadId,
    Guid? CustomerId,
    string Subject,
    string Status,
    int MessageCount,
    DateTime FirstMessageAtUtc,
    DateTime LastMessageAtUtc,
    int UnreadCount,
    string? LastSenderAddress,
    string? LastSenderName,
    string? Snippet,
    bool HasAttachments,
    Delivery.DeliveryNoticeSummary? LatestDeliveryNotice = null
);

/// <summary>No leídos de la oficina para los badges: hilos con algo sin leer y mensajes sin leer.</summary>
public sealed record UnreadMailCount(int Threads, int Messages);
