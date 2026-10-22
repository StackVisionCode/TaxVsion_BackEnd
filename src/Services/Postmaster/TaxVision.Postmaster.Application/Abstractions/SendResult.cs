using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Abstractions;

public enum RecipientSendStatus
{
    Sent,
    Rejected,
}

/// <summary>Resultado de intentar enviar a un destinatario individual dentro del mismo envelope SMTP.</summary>
public sealed record RecipientSendOutcome(
    Guid RecipientId,
    string Address,
    RecipientSendStatus Status,
    string? ErrorReason
);

/// <summary>
/// Resultado agregado de <see cref="IEmailSender.SendAsync"/>. <see cref="Success"/> es true cuando
/// el MTA aceptó el envelope para al menos un destinatario; los rechazos 5xx individuales quedan en
/// <see cref="RecipientOutcomes"/> sin fallar el envío completo (plan §Fase 3, punto 3).
/// <see cref="RetryAfter"/> != null: rechazo pasajero por cupo (429), nada salió y conviene reintentar.
/// </summary>
/// <param name="ProviderMessageId">Message-ID RFC del correo enviado.</param>
/// <param name="ProviderThreadId">Hilo en el buzón (Gmail threadId / Graph conversationId); null por SMTP.</param>
/// <param name="ProviderAcceptance">Respuesta del relay al aceptar (ej. "250 OK id=..."), para auditoría.</param>
/// <param name="ProviderReference">Id propio del proveedor sacado de esa respuesta, si lo trae.</param>
/// <param name="InternetMessageId">Message-ID RFC con el que salió de verdad; los avisos de no entrega lo citan.</param>
/// <param name="MailboxFailure">Solo vía buzón: de quién fue la culpa del fallo (decide si se reintenta por el sistema).</param>
public sealed record SendResult(
    bool Success,
    string? ProviderMessageId,
    string? ErrorReason,
    IReadOnlyList<RecipientSendOutcome> RecipientOutcomes,
    TimeSpan? RetryAfter = null,
    string? ProviderThreadId = null,
    string? ProviderAcceptance = null,
    string? ProviderReference = null,
    string? InternetMessageId = null,
    MailboxSendFailure? MailboxFailure = null
);
