namespace TaxVision.Postmaster.Application.Sending.Commands.MarkSendAttempted;

/// <summary>Graba y confirma el intento justo antes de llamar al proveedor (PMS-01).</summary>
public sealed record MarkSendAttemptedCommand(Guid SentMessageId);
