namespace TaxVision.Postmaster.Application.Sending.Commands.ReleaseSendAttempt;

/// <summary>El proveedor dijo que no salió y que se reintente más tarde (cupo): se libera el intento.</summary>
public sealed record ReleaseSendAttemptCommand(Guid SentMessageId);
