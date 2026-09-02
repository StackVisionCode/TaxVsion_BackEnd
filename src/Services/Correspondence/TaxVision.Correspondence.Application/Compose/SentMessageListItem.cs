namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Fila de la carpeta "Sent" del cliente final (<c>GET /correspondence/sent?customerId=</c>): un
/// mensaje ya enviado (<see cref="Domain.Compose.DraftStatus.Sent"/>). Lean — el body se pide aparte
/// con <c>GET /correspondence/drafts/{id}</c> al abrirlo. <see cref="EmailThreadId"/> es null para un
/// envío nuevo (compose) y no-null para un reply (ver <c>Draft.EmailThreadId</c>): el front lo usa
/// para decidir si al abrirlo muestra el hilo completo o el mensaje suelto.
/// </summary>
public sealed record SentMessageListItem(
    Guid MessageId,
    Guid? EmailThreadId,
    string Subject,
    IReadOnlyList<string> ToAddresses,
    bool IsReply,
    DateTime SentAtUtc,
    bool HasAttachments,
    int AttachmentCount
);
