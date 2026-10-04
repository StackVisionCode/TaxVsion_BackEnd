namespace TaxVision.Signature.Application.Requests.Commands.ScheduleSend;

/// <summary>F3 — Programa el envío futuro de una solicitud en Draft. El cliente envía UTC.</summary>
public sealed record ScheduleSendCommand(Guid TenantId, Guid SignatureRequestId, DateTime ScheduledSendAtUtc);
