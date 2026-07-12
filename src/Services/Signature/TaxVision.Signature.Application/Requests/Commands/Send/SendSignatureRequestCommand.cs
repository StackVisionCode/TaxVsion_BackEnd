namespace TaxVision.Signature.Application.Requests.Commands.Send;

public sealed record SendSignatureRequestCommand(Guid TenantId, Guid SignatureRequestId);
