namespace TaxVision.Signature.Application.Requests.Commands.ResendSignerInvitation;

/// <summary>
/// Staff re-envía la invitación a un firmante pendiente. Emite un nuevo token
/// (misma <c>RevocationEpoch</c> — no invalida los tokens de otros firmantes) y
/// publica un <c>SignerInvited</c> para que Notification dispatche el correo.
/// </summary>
public sealed record ResendSignerInvitationCommand(Guid TenantId, Guid SignatureRequestId, Guid SignerId);
