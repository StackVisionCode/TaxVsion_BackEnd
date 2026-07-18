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
/// </summary>
public sealed record SendResult(
    bool Success,
    string? ProviderMessageId,
    string? ErrorReason,
    IReadOnlyList<RecipientSendOutcome> RecipientOutcomes
);
