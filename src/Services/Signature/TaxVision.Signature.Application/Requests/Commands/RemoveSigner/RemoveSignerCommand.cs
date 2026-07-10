namespace TaxVision.Signature.Application.Requests.Commands.RemoveSigner;

public sealed record RemoveSignerCommand(Guid TenantId, Guid SignatureRequestId, Guid SignerId);
