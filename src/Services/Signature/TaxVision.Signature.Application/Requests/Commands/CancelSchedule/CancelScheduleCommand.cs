namespace TaxVision.Signature.Application.Requests.Commands.CancelSchedule;

/// <summary>F3 — Cancela la programación de envío y la devuelve a Draft.</summary>
public sealed record CancelScheduleCommand(Guid TenantId, Guid SignatureRequestId);
