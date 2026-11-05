namespace TaxVision.Postmaster.Application.Sending.Commands.DiscardUnsentMessage;

/// <summary>
/// Falló antes de salir al proveedor (no se pudo bajar el adjunto): se borra el SentMessage y se libera la
/// reserva para que el mismo draft reintente limpio.
/// </summary>
public sealed record DiscardUnsentMessageCommand(Guid SentMessageId);
