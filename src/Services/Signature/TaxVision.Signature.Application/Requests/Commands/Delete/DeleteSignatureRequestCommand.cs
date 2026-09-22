namespace TaxVision.Signature.Application.Requests.Commands.Delete;

/// <summary>Borrado permanente de un borrador sin enviar (Draft/Ready).</summary>
public sealed record DeleteSignatureRequestCommand(Guid TenantId, Guid SignatureRequestId);
