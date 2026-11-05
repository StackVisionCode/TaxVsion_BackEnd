using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;

/// <summary>Paso transaccional del envío de Correspondence (PMS-01): reserva, cuenta, SentMessage en Sending.</summary>
public sealed record PrepareCorrespondenceMessageCommand(SendCorrespondenceMessageCommand Request);

/// <summary>
/// Listo para enviar (<see cref="Message"/> y <see cref="Mailbox"/>) o ya resuelto sin enviar nada
/// (<see cref="Completed"/>: replay del mismo draft o envío en curso).
/// </summary>
public sealed record PreparedCorrespondenceMessage(
    SentMessage? Message,
    ResolvedMailbox? Mailbox,
    SendCorrespondenceMessageResult? Completed
);
