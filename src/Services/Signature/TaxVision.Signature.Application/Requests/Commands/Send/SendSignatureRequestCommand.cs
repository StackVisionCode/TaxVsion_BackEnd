namespace TaxVision.Signature.Application.Requests.Commands.Send;

/// <param name="SentByUserId">Staff que envía; null en envíos automáticos (programado, desde formulario).</param>
public sealed record SendSignatureRequestCommand(Guid TenantId, Guid SignatureRequestId, Guid? SentByUserId = null);
